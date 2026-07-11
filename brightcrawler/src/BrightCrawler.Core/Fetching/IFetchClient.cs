namespace BrightCrawler.Core.Fetching;

public interface IFetchClient
{
    Task<FetchResult> FetchAsync(string url, CancellationToken cancellationToken);
}
