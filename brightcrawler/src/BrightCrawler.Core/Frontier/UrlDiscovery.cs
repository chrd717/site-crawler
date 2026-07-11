namespace BrightCrawler.Core.Frontier;

public sealed record UrlDiscovery
{
    public required string CanonicalUrl { get; init; }
    public required byte[] CanonicalUrlHash { get; init; }
    public required string FirstSeenUrl { get; init; }
    public required int Depth { get; init; }
    public required string RelationKind { get; init; }
    public required string RawReference { get; init; }
}
