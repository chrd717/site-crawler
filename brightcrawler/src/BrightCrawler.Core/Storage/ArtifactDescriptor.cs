namespace BrightCrawler.Core.Storage;

public sealed record ArtifactDescriptor
{
    public required string RelativePath { get; init; }
    public required byte[] ContentSha256 { get; init; }
    public required long ActualLength { get; init; }
}
