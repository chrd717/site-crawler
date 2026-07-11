using System.Text.Json;
using BrightCrawler.Core.Content;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace BrightCrawler.Infrastructure.Content;

public sealed class ImageContentProcessor : IContentProcessor
{
    public bool CanProcess(string mediaType) => mediaType.StartsWith("image/", StringComparison.Ordinal);

    public ValueTask<ContentProcessingResult> ProcessAsync(
        ContentInput input,
        CancellationToken cancellationToken)
    {
        int? width = null;
        int? height = null;

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(new MemoryStream(input.Body));
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            width = subIfd?.GetInt32(ExifDirectoryBase.TagImageWidth);
            height = subIfd?.GetInt32(ExifDirectoryBase.TagImageHeight);
        }
        catch
        {
            // Best-effort metadata extraction.
        }

        var metadata = JsonSerializer.Serialize(new
        {
            width,
            height,
            fileSizeBytes = input.Body.Length
        });

        return ValueTask.FromResult(new ContentProcessingResult
        {
            Kind = ContentKind.Image,
            MetadataJson = metadata
        });
    }
}
