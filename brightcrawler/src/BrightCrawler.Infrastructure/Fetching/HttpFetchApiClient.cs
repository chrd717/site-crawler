using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrightCrawler.Core.Fetching;
using BrightCrawler.Core.Policies;
using Microsoft.Extensions.Logging;

namespace BrightCrawler.Infrastructure.Fetching;

public sealed class HttpFetchApiClient : IFetchClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpFetchApiClient> _logger;

    public HttpFetchApiClient(HttpClient httpClient, ILogger<HttpFetchApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<FetchResult> FetchAsync(string url, CancellationToken cancellationToken)
    {
        var requestUri = $"/fetch?url={Uri.EscapeDataString(url)}";
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<FetchApiPayload>(
            cancellationToken: cancellationToken);

        if (payload is null)
        {
            throw new InvalidOperationException("Fetch API returned an empty payload.");
        }

        byte[]? body = null;
        if (payload.Body is { } bodyElement)
        {
            body = bodyElement.ValueKind switch
            {
                JsonValueKind.String => System.Text.Encoding.UTF8.GetBytes(bodyElement.GetString()!),
                JsonValueKind.Array => bodyElement.EnumerateArray()
                    .Select(e => (byte)e.GetInt32())
                    .ToArray(),
                _ => JsonSerializer.SerializeToUtf8Bytes(bodyElement)
            };
        }

        var headers = payload.Headers ?? new Dictionary<string, string>();
        _logger.LogDebug("Fetched {Url} status {Status}", url, payload.StatusCode);

        return new FetchResult
        {
            StatusCode = payload.StatusCode,
            Headers = headers,
            Body = body
        };
    }

    private sealed record FetchApiPayload
    {
        [JsonPropertyName("statusCode")]
        public int StatusCode { get; init; }

        [JsonPropertyName("headers")]
        public Dictionary<string, string>? Headers { get; init; }

        [JsonPropertyName("body")]
        public JsonElement? Body { get; init; }
    }
}
