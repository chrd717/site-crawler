using System.Text.Json;
using BrightCrawler.Core.Content;

namespace BrightCrawler.Infrastructure.Content;

public sealed class VideoContentProcessor : IContentProcessor
{
    public bool CanProcess(string mediaType) => mediaType.StartsWith("video/", StringComparison.Ordinal);

    public ValueTask<ContentProcessingResult> ProcessAsync(
        ContentInput input,
        CancellationToken cancellationToken)
    {
        var metadata = JsonSerializer.Serialize(new
        {
            fileSizeBytes = input.Body.Length,
            durationSeconds = (double?)null
        });

        return ValueTask.FromResult(new ContentProcessingResult
        {
            Kind = ContentKind.Video,
            MetadataJson = metadata
        });
    }
}
