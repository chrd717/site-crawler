namespace BrightCrawler.Core.Runs;

public sealed record CrawlOptions
{
    public int MaxConcurrency { get; init; } = 4;
    public int MaxDepth { get; init; } = 10;
    public int MaxUrls { get; init; } = 10_000;
    public long MaxBodyBytes { get; init; } = 50 * 1024 * 1024;
    public long MaxTotalDownloadedBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public int MaxAttemptsPerUrl { get; init; } = 5;
    public int MaxRedirects { get; init; } = 5;
    public TimeSpan MaxRunDuration { get; init; } = TimeSpan.FromHours(24);
    public int RequestRatePerSecond { get; init; } = 10;
    public int RequestBurstSize { get; init; } = 20;
    public TimeSpan FetchTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ProcessingTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(5);
    public string OutputRoot { get; init; } = "output";
    public string FetchApiBaseUrl { get; init; } = "http://mock-api.mock.com";
    public IReadOnlyList<string> AllowedHosts { get; init; } = [];
}
