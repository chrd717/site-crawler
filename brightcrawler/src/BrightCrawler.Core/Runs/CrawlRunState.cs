namespace BrightCrawler.Core.Runs;

public enum CrawlRunState
{
    Created,
    Running,
    Paused,
    Completed,
    CompletedWithFailures,
    StoppedByBudget,
    Failed
}
