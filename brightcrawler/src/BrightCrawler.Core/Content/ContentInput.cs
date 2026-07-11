namespace BrightCrawler.Core.Content;

public sealed record ContentInput
{
    public required string CanonicalUrl { get; init; }
    public required string MediaType { get; init; }
    public required byte[] Body { get; init; }
}
