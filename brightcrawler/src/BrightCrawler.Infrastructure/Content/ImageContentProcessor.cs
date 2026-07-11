using System.Text.Json;
using BrightCrawler.Core.Content;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;

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

            var jpeg = directories.OfType<JpegDirectory>().FirstOrDefault();
            if (jpeg is not null)
            {
                width = jpeg.GetImageWidth();
                height = jpeg.GetImageHeight();
            }

            var png = directories.OfType<PngDirectory>().FirstOrDefault();
            if (png is not null)
            {
                width ??= png.GetInt32(PngDirectory.TagImageWidth);
                height ??= png.GetInt32(PngDirectory.TagImageHeight);
            }

            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            width ??= subIfd?.GetInt32(ExifDirectoryBase.TagImageWidth);
            height ??= subIfd?.GetInt32(ExifDirectoryBase.TagImageHeight);
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
