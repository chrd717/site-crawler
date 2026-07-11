using System.Globalization;
using BrightCrawler.Core.Fetching;
using BrightCrawler.Core.Frontier;

namespace BrightCrawler.Core.Policies;

public enum FetchDecisionKind
{
    Success,
    Redirect,
    Terminal,
    Retry
}

public sealed record FetchDecision
{
    public required FetchDecisionKind Kind { get; init; }
    public string? Location { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public FrontierState? TerminalState { get; init; }
}

public static class FetchOutcomePolicy
{
    public static FetchDecision Classify(
        FetchResult result,
        Content.ContentProcessorRegistry registry,
        long maxBodyBytes,
        int attemptNumber,
        int maxAttempts)
    {
        return result.StatusCode switch
        {
            200 when result.Body is null or { Length: 0 } => TransientOrPermanent(
                attemptNumber, maxAttempts, "empty_body", "200 response with empty body"),
            200 => ClassifySuccess(result, registry, maxBodyBytes),
            >= 300 and < 400 => new FetchDecision
            {
                Kind = FetchDecisionKind.Redirect,
                Location = GetHeader(result, "Location")
            },
            403 => Terminal(FrontierState.Blocked, "blocked", "Access forbidden"),
            404 => Terminal(FrontierState.NotFound, "not_found", "Resource not found"),
            429 => Retry("rate_limited", "Rate limited by upstream API"),
            >= 500 => TransientOrPermanent(
                attemptNumber, maxAttempts, "server_error", $"Server error {result.StatusCode}"),
            _ => Terminal(FrontierState.FailedPermanent, "unexpected_status",
                $"Unexpected status code {result.StatusCode}")
        };
    }

    public static FetchDecision ClassifyTransportFailure(
        int attemptNumber,
        int maxAttempts,
        string code,
        string message) =>
        TransientOrPermanent(attemptNumber, maxAttempts, code, message);

    private static FetchDecision ClassifySuccess(
        FetchResult result,
        Content.ContentProcessorRegistry registry,
        long maxBodyBytes)
    {
        var mediaType = Content.ContentProcessorRegistry.NormalizeMediaType(
            GetHeader(result, "Content-Type") ?? "application/octet-stream");

        if (result.Body!.Length > maxBodyBytes)
        {
            return Terminal(FrontierState.Rejected, "body_too_large",
                $"Body exceeds limit of {maxBodyBytes} bytes");
        }

        if (registry.Resolve(mediaType) is null)
        {
            return Terminal(FrontierState.Unsupported, "unsupported_media_type",
                $"Unsupported media type: {mediaType}");
        }

        return new FetchDecision { Kind = FetchDecisionKind.Success };
    }

    private static FetchDecision TransientOrPermanent(
        int attemptNumber,
        int maxAttempts,
        string code,
        string message)
    {
        if (attemptNumber >= maxAttempts)
        {
            return Terminal(FrontierState.FailedPermanent, code, message);
        }

        return Retry(code, message);
    }

    private static FetchDecision Terminal(FrontierState state, string code, string message) =>
        new()
        {
            Kind = FetchDecisionKind.Terminal,
            TerminalState = state,
            ErrorCode = code,
            ErrorMessage = message
        };

    private static FetchDecision Retry(string code, string message) =>
        new()
        {
            Kind = FetchDecisionKind.Retry,
            ErrorCode = code,
            ErrorMessage = message
        };

    private static string? GetHeader(FetchResult result, string name)
    {
        foreach (var (key, value) in result.Headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }
}

public static class RetryPlanner
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    public static DateTimeOffset PlanRetry(
        TimeProvider timeProvider,
        int attemptNumber,
        FetchResult? result = null,
        string? retryAfterRaw = null)
    {
        var now = timeProvider.GetUtcNow();

        if (result?.StatusCode == 429)
        {
            var retryAfter = ParseRetryAfter(retryAfterRaw, now);
            var backoff = ComputeBackoff(attemptNumber);
            return Max(now + backoff, retryAfter);
        }

        return now + ComputeBackoff(attemptNumber);
    }

    public static DateTimeOffset PlanTransportRetry(TimeProvider timeProvider, int attemptNumber) =>
        timeProvider.GetUtcNow() + ComputeBackoff(attemptNumber);

    public static string? GetRetryAfterHeader(FetchResult result) =>
        result.Headers
            .FirstOrDefault(h => string.Equals(h.Key, "Retry-After", StringComparison.OrdinalIgnoreCase))
            .Value;

    private static DateTimeOffset ParseRetryAfter(string? raw, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return now;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return now.AddSeconds(seconds);
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            return date.ToUniversalTime();
        }

        return now;
    }

    private static TimeSpan ComputeBackoff(int attemptNumber)
    {
        var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, Math.Min(attemptNumber, 6)));
        var jitterMs = Random.Shared.Next(0, 1000);
        var delay = baseDelay + TimeSpan.FromMilliseconds(jitterMs);
        return delay > MaxBackoff ? MaxBackoff : delay;
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) =>
        a > b ? a : b;
}
