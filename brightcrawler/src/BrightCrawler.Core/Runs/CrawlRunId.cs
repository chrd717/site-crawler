namespace BrightCrawler.Core.Runs;

public readonly record struct CrawlRunId(Guid Value)
{
    public static CrawlRunId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
