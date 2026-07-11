using System.Text.Json;
using BrightCrawler.Core.Content;
using BrightCrawler.Core.Fetching;
using BrightCrawler.Core.Frontier;
using BrightCrawler.Core.Policies;
using BrightCrawler.Core.RateControl;
using BrightCrawler.Core.Runs;
using BrightCrawler.Core.Storage;
using Microsoft.Extensions.Logging;

namespace BrightCrawler.Core.Crawling;

public sealed class UrlProcessingPipeline
{
    private readonly IFetchClient _fetchClient;
    private readonly IArtifactStore _artifactStore;
    private readonly IOutboundRequestGate _requestGate;
    private readonly ICrawlFrontier _frontier;
    private readonly ContentProcessorRegistry _processorRegistry;
    private readonly CrawlScope _scope;
    private readonly CrawlOptions _options;
    private readonly CrawlBudget _budget;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UrlProcessingPipeline> _logger;

    public UrlProcessingPipeline(
        IFetchClient fetchClient,
        IArtifactStore artifactStore,
        IOutboundRequestGate requestGate,
        ICrawlFrontier frontier,
        ContentProcessorRegistry processorRegistry,
        CrawlScope scope,
        CrawlOptions options,
        CrawlBudget budget,
        TimeProvider timeProvider,
        ILogger<UrlProcessingPipeline> logger)
    {
        _fetchClient = fetchClient;
        _artifactStore = artifactStore;
        _requestGate = requestGate;
        _frontier = frontier;
        _processorRegistry = processorRegistry;
        _scope = scope;
        _options = options;
        _budget = budget;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ProcessAsync(FrontierLease lease, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.FetchTimeout + _options.ProcessingTimeout);

        try
        {
            await _requestGate.WaitAsync(timeoutCts.Token);
            FetchResult fetchResult;
            try
            {
                fetchResult = await _fetchClient.FetchAsync(lease.CanonicalUrl, timeoutCts.Token);
            }
            finally
            {
                _requestGate.Release();
            }

            var decision = FetchOutcomePolicy.Classify(
                fetchResult,
                _processorRegistry,
                _options.MaxBodyBytes,
                lease.AttemptNumber,
                _options.MaxAttemptsPerUrl);

            switch (decision.Kind)
            {
                case FetchDecisionKind.Success:
                    await HandleSuccessAsync(lease, fetchResult, timeoutCts.Token);
                    break;
                case FetchDecisionKind.Redirect:
                    await HandleRedirectAsync(lease, fetchResult, decision, timeoutCts.Token);
                    break;
                case FetchDecisionKind.Retry:
                    await HandleRetryAsync(lease, fetchResult, decision, timeoutCts.Token);
                    break;
                case FetchDecisionKind.Terminal:
                    await HandleTerminalAsync(lease, fetchResult, decision, timeoutCts.Token);
                    break;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var plan = new RetryPlan
            {
                HttpStatus = 0,
                ErrorCode = "timeout",
                ErrorMessage = "Fetch or processing timed out",
                AvailableAt = RetryPlanner.PlanTransportRetry(_timeProvider, lease.AttemptNumber)
            };
            await _frontier.ScheduleRetryAsync(lease, plan, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            var decision = FetchOutcomePolicy.ClassifyTransportFailure(
                lease.AttemptNumber,
                _options.MaxAttemptsPerUrl,
                "transport_error",
                Truncate(ex.Message));

            if (decision.Kind == FetchDecisionKind.Terminal)
            {
                await _frontier.CompleteTerminalAsync(lease, new TerminalOutcome
                {
                    State = decision.TerminalState!.Value,
                    ErrorCode = decision.ErrorCode!,
                    ErrorMessage = decision.ErrorMessage!
                }, cancellationToken);
            }
            else
            {
                await _frontier.ScheduleRetryAsync(lease, new RetryPlan
                {
                    HttpStatus = 0,
                    ErrorCode = decision.ErrorCode!,
                    ErrorMessage = decision.ErrorMessage!,
                    AvailableAt = RetryPlanner.PlanTransportRetry(_timeProvider, lease.AttemptNumber)
                }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected failure processing {Url}", lease.CanonicalUrl);
            await _frontier.CompleteTerminalAsync(lease, new TerminalOutcome
            {
                State = FrontierState.FailedPermanent,
                ErrorCode = "unexpected_error",
                ErrorMessage = Truncate(ex.Message)
            }, cancellationToken);
        }
    }

    private async Task HandleSuccessAsync(
        FrontierLease lease,
        FetchResult fetchResult,
        CancellationToken cancellationToken)
    {
        var mediaType = ContentProcessorRegistry.NormalizeMediaType(
            fetchResult.Headers.FirstOrDefault(h =>
                string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)).Value
            ?? "application/octet-stream");

        var processor = _processorRegistry.Resolve(mediaType)!;
        ContentProcessingResult processed;
        try
        {
            processed = await processor.ProcessAsync(new ContentInput
            {
                CanonicalUrl = lease.CanonicalUrl,
                MediaType = mediaType,
                Body = fetchResult.Body!
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            await _frontier.CompleteTerminalAsync(lease, new TerminalOutcome
            {
                State = FrontierState.InvalidContent,
                HttpStatus = 200,
                MediaType = mediaType,
                ActualLength = fetchResult.Body!.Length,
                ErrorCode = "invalid_content",
                ErrorMessage = Truncate(ex.Message)
            }, cancellationToken);
            return;
        }

        if (!_budget.TryReserveBytes(fetchResult.Body!.Length))
        {
            await _frontier.CompleteTerminalAsync(lease, new TerminalOutcome
            {
                State = FrontierState.Rejected,
                HttpStatus = 200,
                ErrorCode = "budget_exceeded",
                ErrorMessage = "Total download budget exceeded"
            }, cancellationToken);
            return;
        }

        var artifact = await _artifactStore.SaveAsync(
            processed.Kind,
            mediaType,
            fetchResult.Body,
            cancellationToken);

        var discoveries = BuildDiscoveries(lease, processed.References);

        var completion = new CrawlCompletion
        {
            HttpStatus = fetchResult.StatusCode,
            MediaType = mediaType,
            DeclaredLength = ParseLongHeader(fetchResult, "Content-Length"),
            ActualLength = artifact.ActualLength,
            ContentSha256 = artifact.ContentSha256,
            ArtifactPath = artifact.RelativePath,
            MetadataJson = processed.MetadataJson,
            ETag = GetHeader(fetchResult, "ETag"),
            LastModified = GetHeader(fetchResult, "Last-Modified")
        };

        var committed = await _frontier.CompleteSuccessAsync(lease, completion, discoveries, cancellationToken);
        if (!committed)
        {
            _logger.LogWarning("Stale lease on success for {Url}", lease.CanonicalUrl);
        }
    }

    private async Task HandleRedirectAsync(
        FrontierLease lease,
        FetchResult fetchResult,
        FetchDecision decision,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(decision.Location))
        {
            await _frontier.CompleteTerminalAsync(lease, new TerminalOutcome
            {
                State = FrontierState.FailedPermanent,
                HttpStatus = fetchResult.StatusCode,
                ErrorCode = "missing_location",
                ErrorMessage = "Redirect without Location header"
            }, cancellationToken);
            return;
        }

        if (lease.Depth >= _options.MaxDepth)
        {
            await _frontier.CompleteTerminalAsync(lease, new TerminalOutcome
            {
                State = FrontierState.Rejected,
                HttpStatus = fetchResult.StatusCode,
                ErrorCode = "max_depth",
                ErrorMessage = "Maximum crawl depth exceeded"
            }, cancellationToken);
            return;
        }

        var resolved = ResolveReference(lease.CanonicalUrl, decision.Location);
        if (!UrlCanonicalizer.TryCanonicalize(resolved, out var targetCanonical, out var targetHash))
        {
            await _frontier.CompleteTerminalAsync(lease, new TerminalOutcome
            {
                State = FrontierState.Rejected,
                HttpStatus = fetchResult.StatusCode,
                ErrorCode = "invalid_redirect",
                ErrorMessage = "Redirect target is not a valid HTTP(S) URL"
            }, cancellationToken);
            return;
        }

        if (!_scope.IsInScope(targetCanonical))
        {
            await _frontier.CompleteTerminalAsync(lease, new TerminalOutcome
            {
                State = FrontierState.Redirected,
                HttpStatus = fetchResult.StatusCode,
                ErrorCode = "out_of_scope_redirect",
                ErrorMessage = "Redirect target is out of scope"
            }, cancellationToken);
            return;
        }

        var redirect = new RedirectCompletion
        {
            HttpStatus = fetchResult.StatusCode,
            Location = decision.Location,
            TargetCanonicalUrl = targetCanonical,
            TargetCanonicalUrlHash = targetHash,
            TargetDepth = lease.Depth + 1
        };

        await _frontier.CompleteRedirectAsync(lease, redirect, cancellationToken);
    }

    private async Task HandleRetryAsync(
        FrontierLease lease,
        FetchResult fetchResult,
        FetchDecision decision,
        CancellationToken cancellationToken)
    {
        var retryAfterRaw = RetryPlanner.GetRetryAfterHeader(fetchResult);
        var availableAt = RetryPlanner.PlanRetry(
            _timeProvider,
            lease.AttemptNumber,
            fetchResult,
            retryAfterRaw);

        if (fetchResult.StatusCode == 429)
        {
            _requestGate.ApplyCooldown(availableAt);
        }

        await _frontier.ScheduleRetryAsync(lease, new RetryPlan
        {
            HttpStatus = fetchResult.StatusCode,
            ErrorCode = decision.ErrorCode!,
            ErrorMessage = decision.ErrorMessage!,
            AvailableAt = availableAt,
            RetryAfterRaw = retryAfterRaw
        }, cancellationToken);
    }

    private async Task HandleTerminalAsync(
        FrontierLease lease,
        FetchResult fetchResult,
        FetchDecision decision,
        CancellationToken cancellationToken)
    {
        await _frontier.CompleteTerminalAsync(lease, new TerminalOutcome
        {
            State = decision.TerminalState!.Value,
            HttpStatus = fetchResult.StatusCode,
            ErrorCode = decision.ErrorCode!,
            ErrorMessage = decision.ErrorMessage!
        }, cancellationToken);
    }

    private IReadOnlyCollection<UrlDiscovery> BuildDiscoveries(
        FrontierLease lease,
        IReadOnlyList<DiscoveredReference> references)
    {
        if (!_budget.CanDiscoverMore())
        {
            return [];
        }

        var discoveries = new List<UrlDiscovery>();
        foreach (var reference in references)
        {
            if (discoveries.Count >= _options.MaxUrls)
            {
                break;
            }

            if (lease.Depth >= _options.MaxDepth)
            {
                break;
            }

            var resolved = ResolveReference(lease.CanonicalUrl, reference.RawUrl);
            if (!UrlCanonicalizer.TryCanonicalize(resolved, out var canonical, out var hash))
            {
                continue;
            }

            if (!_scope.IsInScope(canonical))
            {
                continue;
            }

            discoveries.Add(new UrlDiscovery
            {
                CanonicalUrl = canonical,
                CanonicalUrlHash = hash,
                FirstSeenUrl = reference.RawUrl,
                Depth = lease.Depth + 1,
                RelationKind = reference.RelationKind,
                RawReference = reference.RawUrl
            });
        }

        return discoveries;
    }

    private static string ResolveReference(string baseUrl, string rawReference)
    {
        if (Uri.TryCreate(rawReference, UriKind.Absolute, out _))
        {
            return rawReference;
        }

        if (Uri.TryCreate(new Uri(baseUrl), rawReference, out var resolved))
        {
            return resolved.ToString();
        }

        return rawReference;
    }

    private static long? ParseLongHeader(FetchResult result, string name)
    {
        var value = GetHeader(result, name);
        return long.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? GetHeader(FetchResult result, string name) =>
        result.Headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string Truncate(string message, int max = 500) =>
        message.Length <= max ? message : message[..max];
}
