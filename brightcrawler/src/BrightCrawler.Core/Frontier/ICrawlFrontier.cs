using BrightCrawler.Core.Runs;

namespace BrightCrawler.Core.Frontier;

public interface ICrawlFrontier
{
    Task<CrawlRunId> CreateRunAsync(
        CrawlRunDefinition definition,
        CancellationToken cancellationToken);

    Task<FrontierLease?> TryLeaseNextAsync(
        CrawlRunId runId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> CompleteSuccessAsync(
        FrontierLease lease,
        CrawlCompletion completion,
        IReadOnlyCollection<UrlDiscovery> discoveries,
        CancellationToken cancellationToken);

    Task<bool> CompleteRedirectAsync(
        FrontierLease lease,
        RedirectCompletion redirect,
        CancellationToken cancellationToken);

    Task<bool> ScheduleRetryAsync(
        FrontierLease lease,
        RetryPlan retry,
        CancellationToken cancellationToken);

    Task<bool> CompleteTerminalAsync(
        FrontierLease lease,
        TerminalOutcome outcome,
        CancellationToken cancellationToken);

    Task<FrontierSnapshot> GetSnapshotAsync(
        CrawlRunId runId,
        CancellationToken cancellationToken);

    Task MarkRunCompletedAsync(
        CrawlRunId runId,
        CrawlRunState state,
        string? stopReason,
        CancellationToken cancellationToken);
}
