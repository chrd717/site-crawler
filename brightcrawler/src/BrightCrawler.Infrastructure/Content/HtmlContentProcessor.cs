using System.Text.Json;
using AngleSharp.Html.Parser;
using BrightCrawler.Core.Content;

namespace BrightCrawler.Infrastructure.Content;

public sealed class HtmlContentProcessor : IContentProcessor
{
    private static readonly string[] Selectors =
    [
        "a[href]", "iframe[src]", "img[src]", "video[src]", "video[poster]",
        "source[src]", "object[data]", "embed[src]", "base[href]"
    ];

    public bool CanProcess(string mediaType) =>
        mediaType is "text/html" or "application/xhtml+xml";

    public async ValueTask<ContentProcessingResult> ProcessAsync(
        ContentInput input,
        CancellationToken cancellationToken)
    {
        var html = System.Text.Encoding.UTF8.GetString(input.Body);
        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html, cancellationToken);
        var title = document.Title?.Trim() ?? string.Empty;

        var references = new List<DiscoveredReference>();
        foreach (var selector in Selectors)
        {
            foreach (var element in document.QuerySelectorAll(selector))
            {
                var attribute = selector.StartsWith("base", StringComparison.Ordinal)
                    ? "href"
                    : selector.Contains("poster", StringComparison.Ordinal)
                        ? "poster"
                        : selector.Contains("data", StringComparison.Ordinal)
                            ? "data"
                            : selector.Contains("src", StringComparison.Ordinal)
                                ? "src"
                                : "href";

                var value = element.GetAttribute(attribute);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                references.Add(new DiscoveredReference
                {
                    RawUrl = value,
                    RelationKind = selector
                });
            }
        }

        var metadata = JsonSerializer.Serialize(new
        {
            title,
            extractedReferenceCount = references.Count
        });

        return new ContentProcessingResult
        {
            Kind = ContentKind.Html,
            MetadataJson = metadata,
            References = references
        };
    }
}
