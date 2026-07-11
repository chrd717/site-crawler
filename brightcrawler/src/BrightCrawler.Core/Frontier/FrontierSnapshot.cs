namespace BrightCrawler.Core.Frontier;

public sealed record FrontierSnapshot
{
    public required int PendingCount { get; init; }
    public required int LeasedCount { get; init; }
    public required int RetryScheduledCount { get; init; }
    public required int TerminalCount { get; init; }
    public DateTimeOffset? NextAvailableAt { get; init; }
    public TimeSpan? OldestPendingAge { get; init; }

    public int ActiveCount => PendingCount + LeasedCount + RetryScheduledCount;

    public bool IsComplete => ActiveCount == 0;
}
