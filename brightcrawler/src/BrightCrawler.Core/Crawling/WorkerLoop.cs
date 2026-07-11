using BrightCrawler.Core.Frontier;
using BrightCrawler.Core.Runs;
using Microsoft.Extensions.Logging;

namespace BrightCrawler.Core.Crawling;

public sealed class WorkerLoop
{
    private readonly ICrawlFrontier _frontier;
    private readonly UrlProcessingPipeline _pipeline;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeSpan _pollDelay;
    private readonly ILogger<WorkerLoop> _logger;
    private readonly string _workerId;

    public WorkerLoop(
        ICrawlFrontier frontier,
        UrlProcessingPipeline pipeline,
        TimeSpan leaseDuration,
        string workerId,
        ILogger<WorkerLoop> logger,
        TimeSpan? pollDelay = null)
    {
        _frontier = frontier;
        _pipeline = pipeline;
        _leaseDuration = leaseDuration;
        _workerId = workerId;
        _logger = logger;
        _pollDelay = pollDelay ?? TimeSpan.FromMilliseconds(250);
    }

    public async Task RunAsync(CrawlRunId runId, Func<FrontierSnapshot, bool> shouldContinue, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var lease = await _frontier.TryLeaseNextAsync(runId, _workerId, _leaseDuration, cancellationToken);
            if (lease is not null)
            {
                _logger.LogInformation(
                    "Leased {Url} attempt {Attempt} worker {Worker}",
                    lease.CanonicalUrl,
                    lease.AttemptNumber,
                    _workerId);

                await _pipeline.ProcessAsync(lease, cancellationToken);
                continue;
            }

            var snapshot = await _frontier.GetSnapshotAsync(runId, cancellationToken);
            if (!shouldContinue(snapshot))
            {
                break;
            }

            if (snapshot.IsComplete)
            {
                break;
            }

            var delay = ComputeWaitDelay(snapshot);
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private TimeSpan ComputeWaitDelay(FrontierSnapshot snapshot)
    {
        if (snapshot.NextAvailableAt is { } next)
        {
            var untilRetry = next - DateTimeOffset.UtcNow;
            if (untilRetry > TimeSpan.Zero)
            {
                return untilRetry < TimeSpan.FromSeconds(5) ? untilRetry : TimeSpan.FromSeconds(5);
            }
        }

        return _pollDelay;
    }
}
