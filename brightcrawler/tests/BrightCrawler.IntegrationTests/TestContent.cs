using System.Text;
using BrightCrawler.Core.Fetching;
using BrightCrawler.Infrastructure.Fetching;

namespace BrightCrawler.IntegrationTests;

internal static class TestContent
{
    public static string HtmlPage(string title, params string[] links) =>
        $"""
        <!DOCTYPE html>
        <html>
        <head><title>{title}</title></head>
        <body>
        {string.Join('\n', links.Select(l => $"""<a href="{l}">link</a>"""))}
        </body>
        </html>
        """;

    public static FetchResult Html(string body, int status = 200) =>
        FetchResultFactory.Html(body, status);

    public static FetchResult RateLimited(int retryAfterSeconds = 1) => new()
    {
        StatusCode = 429,
        Headers = new Dictionary<string, string>
        {
            ["Retry-After"] = retryAfterSeconds.ToString()
        },
        Body = null
    };

    public static FetchResult Pdf(string urlPath) => FetchResultFactory.Pdf(MinimalPdfBytes);

    public static readonly byte[] MinimalPdfBytes = Encoding.ASCII.GetBytes(
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
}
