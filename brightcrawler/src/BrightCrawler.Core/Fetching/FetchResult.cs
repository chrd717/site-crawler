namespace BrightCrawler.Core.Fetching;

public sealed record FetchResult
{
    public required int StatusCode { get; init; }
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
    public byte[]? Body { get; init; }
}
