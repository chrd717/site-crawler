namespace BrightCrawler.Core.Runs;

public sealed record CrawlRunInfo
{
    public required CrawlRunId RunId { get; init; }
    public required CrawlRunDefinition Definition { get; init; }
    public required CrawlRunState State { get; init; }
    public required int KnownUrlCount { get; init; }
    public required long DownloadedBytes { get; init; }

    public bool CanResume =>
        State is CrawlRunState.Paused
            or CrawlRunState.Running
            or CrawlRunState.CompletedWithFailures;
}
