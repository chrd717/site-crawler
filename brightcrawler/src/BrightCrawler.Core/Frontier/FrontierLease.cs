using BrightCrawler.Core.Runs;

namespace BrightCrawler.Core.Frontier;

public sealed record FrontierLease
{
    public required CrawlRunId RunId { get; init; }
    public required FrontierEntryId EntryId { get; init; }
    public required Guid LeaseToken { get; init; }
    public required string WorkerId { get; init; }
    public required int AttemptNumber { get; init; }
    public required string CanonicalUrl { get; init; }
    public required int Depth { get; init; }
    public required DateTimeOffset LeaseUntil { get; init; }
}
