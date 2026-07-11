using BrightCrawler.Core.Content;

namespace BrightCrawler.Core.Storage;

public interface IArtifactStore
{
    Task<ArtifactDescriptor> SaveAsync(
        ContentKind kind,
        string mediaType,
        byte[] body,
        CancellationToken cancellationToken);
}
