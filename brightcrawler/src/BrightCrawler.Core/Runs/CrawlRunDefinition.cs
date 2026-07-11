namespace BrightCrawler.Core.Runs;

public sealed record CrawlRunDefinition
{
    public required string SeedUrl { get; init; }
    public required string EffectiveHost { get; init; }
    public required CrawlOptions Options { get; init; }
}
