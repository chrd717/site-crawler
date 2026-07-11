using BrightCrawler.Core.Crawling;
using BrightCrawler.Core.Frontier;
using BrightCrawler.Core.Policies;
using BrightCrawler.Core.Runs;
using Microsoft.Extensions.Logging;

namespace BrightCrawler.Core.Crawling;

public sealed class CrawlCoordinator
{
    private readonly ICrawlFrontier _frontier;
    private readonly Func<CrawlRunContext, UrlProcessingPipeline> _pipelineFactory;
    private readonly ILogger<CrawlCoordinator> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public CrawlCoordinator(
        ICrawlFrontier frontier,
        Func<CrawlRunContext, UrlProcessingPipeline> pipelineFactory,
        ILogger<CrawlCoordinator> logger,
        ILoggerFactory loggerFactory)
    {
        _frontier = frontier;
        _pipelineFactory = pipelineFactory;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<CrawlRunId> RunAsync(CrawlRunDefinition definition, CancellationToken cancellationToken)
    {
        var runId = await _frontier.CreateRunAsync(definition, cancellationToken);
        _logger.LogInformation("Started crawl run {RunId} for seed {Seed}", runId, definition.SeedUrl);

        var context = new CrawlRunContext(definition);
        await ExecuteWorkersAsync(runId, context, definition.Options, cancellationToken);
        return runId;
    }

    public async Task<CrawlRunId> ResumeAsync(
        CrawlRunId runId,
        CrawlOptions? optionsOverride,
        CancellationToken cancellationToken)
    {
        var info = await _frontier.GetRunAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException($"Crawl run {runId} was not found.");

        if (!info.CanResume)
        {
            throw new InvalidOperationException(
                $"Crawl run {runId} is in state {info.State} and cannot be resumed.");
        }

        var snapshot = await _frontier.GetSnapshotAsync(runId, cancellationToken);
        if (snapshot.IsComplete)
        {
            throw new InvalidOperationException($"Crawl run {runId} has no remaining work.");
        }

        if (!await _frontier.PrepareResumeAsync(runId, cancellationToken))
        {
            throw new InvalidOperationException($"Crawl run {runId} could not be prepared for resume.");
        }

        var definition = optionsOverride is null
            ? info.Definition
            : info.Definition with { Options = optionsOverride };

        _logger.LogInformation("Resuming crawl run {RunId} for seed {Seed}", runId, definition.SeedUrl);

        var context = new CrawlRunContext(
            definition,
            info.KnownUrlCount,
            info.DownloadedBytes);
        await ExecuteWorkersAsync(runId, context, definition.Options, cancellationToken);
        return runId;
    }

    private async Task ExecuteWorkersAsync(
        CrawlRunId runId,
        CrawlRunContext context,
        CrawlOptions options,
        CancellationToken cancellationToken)
    {
        var workers = new List<Task>();
        for (var i = 0; i < options.MaxConcurrency; i++)
        {
            var workerId = $"worker-{i + 1}";
            var pipeline = _pipelineFactory(context);
            var worker = new WorkerLoop(
                _frontier,
                pipeline,
                options.LeaseDuration,
                workerId,
                _loggerFactory.CreateLogger<WorkerLoop>());

            workers.Add(worker.RunAsync(runId, _ => true, cancellationToken));
        }

        await Task.WhenAll(workers);

        var snapshot = await _frontier.GetSnapshotAsync(runId, cancellationToken);
        var state = snapshot.IsComplete
            ? CrawlRunState.Completed
            : cancellationToken.IsCancellationRequested
                ? CrawlRunState.Paused
                : CrawlRunState.CompletedWithFailures;

        await _frontier.MarkRunCompletedAsync(runId, state, null, cancellationToken);

        _logger.LogInformation(
            "Crawl run {RunId} finished. Terminal={Terminal} Active={Active}",
            runId,
            snapshot.TerminalCount,
            snapshot.ActiveCount);
    }
}

public sealed class CrawlRunContext
{
    public CrawlRunContext(CrawlRunDefinition definition)
        : this(definition, knownUrlCount: 1, downloadedBytes: 0)
    {
    }

    public CrawlRunContext(CrawlRunDefinition definition, int knownUrlCount, long downloadedBytes)
    {
        Definition = definition;
        Scope = new CrawlScope(definition.EffectiveHost, definition.Options.AllowedHosts);
        Budget = new CrawlBudget(
            definition.Options.MaxUrls,
            definition.Options.MaxTotalDownloadedBytes,
            definition.Options.MaxDepth);
        Budget.InitializeFromRun(knownUrlCount, downloadedBytes);
    }

    public CrawlRunDefinition Definition { get; }
    public CrawlScope Scope { get; }
    public CrawlBudget Budget { get; }
}
