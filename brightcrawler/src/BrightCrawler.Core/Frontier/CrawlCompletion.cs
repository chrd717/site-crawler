namespace BrightCrawler.Core.Frontier;

public sealed record CrawlCompletion
{
    public required int HttpStatus { get; init; }
    public required string MediaType { get; init; }
    public long? DeclaredLength { get; init; }
    public required long ActualLength { get; init; }
    public required byte[] ContentSha256 { get; init; }
    public required string ArtifactPath { get; init; }
    public required string MetadataJson { get; init; }
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
}
