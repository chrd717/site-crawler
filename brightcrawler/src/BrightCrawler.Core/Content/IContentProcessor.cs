namespace BrightCrawler.Core.Content;

public interface IContentProcessor
{
    bool CanProcess(string mediaType);

    ValueTask<ContentProcessingResult> ProcessAsync(
        ContentInput input,
        CancellationToken cancellationToken);
}
