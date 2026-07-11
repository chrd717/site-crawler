namespace BrightCrawler.Core.Frontier;

public sealed record RedirectCompletion
{
    public required int HttpStatus { get; init; }
    public required string Location { get; init; }
    public required string TargetCanonicalUrl { get; init; }
    public required byte[] TargetCanonicalUrlHash { get; init; }
    public required int TargetDepth { get; init; }
}
