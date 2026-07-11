using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://+:8080");

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/fetch", (HttpRequest request) =>
{
    var targetUrl = request.Query["url"].ToString();
    if (string.IsNullOrWhiteSpace(targetUrl))
    {
        return Results.BadRequest(new { error = "Missing required query parameter: url" });
    }

    if (!DemoSite.TryGetResponse(targetUrl, out var response))
    {
        return Results.Json(
            new FetchResponse
            {
                StatusCode = 404,
                Headers = new Dictionary<string, string> { ["Content-Type"] = "text/plain" },
                Body = "Not found in demo site"
            });
    }

    return Results.Json(response);
});

app.Run();

internal static class DemoSite
{
    private const string Home = "https://example.com/";
    private const string About = "https://example.com/about";
    private const string Logo = "https://example.com/assets/logo.png";
    private const string Video = "https://example.com/media/clip.mp4";
    private const string Guide = "https://example.com/docs/guide.pdf";

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly byte[] MinimalPdf = Encoding.ASCII.GetBytes(
        """
        %PDF-1.4
        1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj
        2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj
        3 0 obj<</Type/Page/MediaBox[0 0 200 200]/Parent 2 0 R/Resources<<>>>>endobj
        xref
        0 4
        0000000000 65535 f 
        0000000009 00000 n 
        0000000052 00000 n 
        0000000101 00000 n 
        trailer<</Size 4/Root 1 0 R>>
        startxref
        178
        %%EOF
        """);

    // Minimal MP4-ish bytes for local demo; duration metadata may stay null.
    private static readonly byte[] MinimalVideo =
    [
        0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70,
        0x69, 0x73, 0x6F, 0x6D, 0x00, 0x00, 0x02, 0x00,
        0x69, 0x73, 0x6F, 0x6D, 0x69, 0x73, 0x6F, 0x32
    ];

    private static readonly Dictionary<string, FetchResponse> Responses = new(StringComparer.Ordinal)
    {
        [Home] = Html(
            "Example Home",
            About,
            Logo,
            Video,
            Guide),
        [About] = Html("About Example"),
        [Logo] = Binary(OnePixelPng, "image/png"),
        [Video] = Binary(MinimalVideo, "video/mp4"),
        [Guide] = Binary(MinimalPdf, "application/pdf")
    };

    public static bool TryGetResponse(string rawUrl, out FetchResponse response)
    {
        response = default!;
        if (!TryNormalize(rawUrl, out var canonical))
        {
            return false;
        }

        if (Responses.TryGetValue(canonical, out var match))
        {
            response = match;
            return true;
        }

        if (Uri.TryCreate(canonical, UriKind.Absolute, out var uri)
            && uri.Host.Equals("example.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        response = new FetchResponse
        {
            StatusCode = 403,
            Headers = new Dictionary<string, string> { ["Content-Type"] = "text/plain" },
            Body = "Forbidden (out of demo scope)"
        };
        return true;
    }

    private static bool TryNormalize(string rawUrl, out string canonical)
    {
        canonical = string.Empty;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.IdnHost.ToLowerInvariant(),
            Fragment = string.Empty
        };

        if ((builder.Scheme == "http" && builder.Port == 80)
            || (builder.Scheme == "https" && builder.Port == 443))
        {
            builder.Port = -1;
        }

        canonical = builder.Uri.GetComponents(
            UriComponents.AbsoluteUri & ~UriComponents.Fragment,
            UriFormat.UriEscaped);

        if (canonical is "https://example.com")
        {
            canonical = Home;
        }

        return true;
    }

    private static FetchResponse Html(string title, params string[] links)
    {
        var body = new StringBuilder();
        body.AppendLine("<!DOCTYPE html>");
        body.AppendLine("<html><head>");
        body.AppendLine($"<title>{title}</title>");
        body.AppendLine("</head><body>");
        foreach (var link in links)
        {
            body.AppendLine($"""<a href="{link}">link</a>""");
        }

        body.AppendLine("</body></html>");

        return new FetchResponse
        {
            StatusCode = 200,
            Headers = new Dictionary<string, string> { ["Content-Type"] = "text/html; charset=utf-8" },
            Body = body.ToString()
        };
    }

    private static FetchResponse Binary(byte[] bytes, string contentType) => new()
    {
        StatusCode = 200,
        Headers = new Dictionary<string, string>
        {
            ["Content-Type"] = contentType,
            ["Content-Length"] = bytes.Length.ToString()
        },
        Body = bytes.Select(static b => (int)b).ToArray()
    };
}

internal sealed class FetchResponse
{
    [JsonPropertyName("statusCode")]
    public required int StatusCode { get; init; }

    [JsonPropertyName("headers")]
    public required Dictionary<string, string> Headers { get; init; }

    [JsonPropertyName("body")]
    public object? Body { get; init; }
}
