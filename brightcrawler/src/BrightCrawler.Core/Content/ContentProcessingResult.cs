namespace BrightCrawler.Core.Content;

public sealed record ContentProcessingResult
{
    public required ContentKind Kind { get; init; }
    public required string MetadataJson { get; init; }
    public IReadOnlyList<DiscoveredReference> References { get; init; } = [];
}
