using System.Text.Json;
using BrightCrawler.Core.Content;
using UglyToad.PdfPig;

namespace BrightCrawler.Infrastructure.Content;

public sealed class PdfContentProcessor : IContentProcessor
{
    public bool CanProcess(string mediaType) => mediaType == "application/pdf";

    public ValueTask<ContentProcessingResult> ProcessAsync(
        ContentInput input,
        CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(input.Body);
        var title = document.Information?.Title;
        var metadata = JsonSerializer.Serialize(new
        {
            pageCount = document.NumberOfPages,
            title
        });

        return ValueTask.FromResult(new ContentProcessingResult
        {
            Kind = ContentKind.Pdf,
            MetadataJson = metadata
        });
    }
}
