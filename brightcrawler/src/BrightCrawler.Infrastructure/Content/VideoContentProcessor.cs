using System.Text.Json;
using BrightCrawler.Core.Content;
using MetadataExtractor;
using MetadataExtractor.Formats.QuickTime;

namespace BrightCrawler.Infrastructure.Content;

public sealed class VideoContentProcessor : IContentProcessor
{
    public bool CanProcess(string mediaType) => mediaType.StartsWith("video/", StringComparison.Ordinal);

    public ValueTask<ContentProcessingResult> ProcessAsync(
        ContentInput input,
        CancellationToken cancellationToken)
    {
        double? durationSeconds = null;

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(new MemoryStream(input.Body));
            var movie = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();
            if (movie is not null
                && movie.ContainsTag(QuickTimeMovieHeaderDirectory.TagDuration)
                && movie.ContainsTag(QuickTimeMovieHeaderDirectory.TagTimeScale))
            {
                var duration = movie.GetInt64(QuickTimeMovieHeaderDirectory.TagDuration);
                var timeScale = movie.GetInt32(QuickTimeMovieHeaderDirectory.TagTimeScale);
                if (timeScale > 0)
                {
                    durationSeconds = duration / (double)timeScale;
                }
            }
        }
        catch
        {
            // Best-effort metadata extraction.
        }

        var metadata = JsonSerializer.Serialize(new
        {
            fileSizeBytes = input.Body.Length,
            durationSeconds
        });

        return ValueTask.FromResult(new ContentProcessingResult
        {
            Kind = ContentKind.Video,
            MetadataJson = metadata
        });
    }
}
