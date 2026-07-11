using System.Collections.Concurrent;
using System.Security.Cryptography;
using BrightCrawler.Core.Content;
using BrightCrawler.Core.Fetching;

namespace BrightCrawler.Infrastructure.Fetching;

public sealed class InMemoryFetchApiClient : IFetchClient
{
    private readonly ConcurrentDictionary<string, Func<FetchResult>> _responses = new(StringComparer.Ordinal);

    public void Register(string url, Func<FetchResult> factory) =>
        _responses[url] = factory;

    public Task<FetchResult> FetchAsync(string url, CancellationToken cancellationToken)
    {
        if (_responses.TryGetValue(url, out var factory))
        {
            return Task.FromResult(factory());
        }

        return Task.FromResult(new FetchResult
        {
            StatusCode = 404,
            Headers = new Dictionary<string, string>(),
            Body = null
        });
    }
}

public static class FetchResultFactory
{
    public static FetchResult Html(string body, int status = 200) => new()
    {
        StatusCode = status,
        Headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/html; charset=utf-8",
            ["Content-Length"] = System.Text.Encoding.UTF8.GetByteCount(body).ToString()
        },
        Body = System.Text.Encoding.UTF8.GetBytes(body)
    };

    public static FetchResult Image(byte[] body, string mediaType = "image/png") => new()
    {
        StatusCode = 200,
        Headers = new Dictionary<string, string>
        {
            ["Content-Type"] = mediaType,
            ["Content-Length"] = body.Length.ToString()
        },
        Body = body
    };

    public static FetchResult Pdf(byte[] body) => new()
    {
        StatusCode = 200,
        Headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/pdf",
            ["Content-Length"] = body.Length.ToString()
        },
        Body = body
    };
}
