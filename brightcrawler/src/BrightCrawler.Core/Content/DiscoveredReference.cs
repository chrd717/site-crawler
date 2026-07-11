namespace BrightCrawler.Core.Content;

public sealed record DiscoveredReference
{
    public required string RawUrl { get; init; }
    public required string RelationKind { get; init; }
}
