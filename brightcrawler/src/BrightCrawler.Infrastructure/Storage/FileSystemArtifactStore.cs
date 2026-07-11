using System.Security.Cryptography;
using BrightCrawler.Core.Content;
using BrightCrawler.Core.Storage;

namespace BrightCrawler.Infrastructure.Storage;

public sealed class FileSystemArtifactStore : IArtifactStore
{
    private readonly string _outputRoot;

    public FileSystemArtifactStore(string outputRoot)
    {
        _outputRoot = outputRoot;
    }

    public async Task<ArtifactDescriptor> SaveAsync(
        ContentKind kind,
        string mediaType,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var hash = SHA256.HashData(body);
        var hashHex = Convert.ToHexString(hash).ToLowerInvariant();
        var shard = hashHex[..2];
        var extension = GetExtension(kind, mediaType);
        var relativePath = Path.Combine(GetFolder(kind), shard, $"{hashHex}{extension}");
        var finalPath = Path.Combine(_outputRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

        if (File.Exists(finalPath))
        {
            return new ArtifactDescriptor
            {
                RelativePath = relativePath.Replace('\\', '/'),
                ContentSha256 = hash,
                ActualLength = body.Length
            };
        }

        var tempPath = finalPath + $".{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(tempPath, body, cancellationToken);
        File.Move(tempPath, finalPath, overwrite: false);

        return new ArtifactDescriptor
        {
            RelativePath = relativePath.Replace('\\', '/'),
            ContentSha256 = hash,
            ActualLength = body.Length
        };
    }

    private static string GetFolder(ContentKind kind) => kind switch
    {
        ContentKind.Html => "html",
        ContentKind.Image => "images",
        ContentKind.Video => "videos",
        ContentKind.Pdf => "pdfs",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string GetExtension(ContentKind kind, string mediaType) => kind switch
    {
        ContentKind.Html => ".html",
        ContentKind.Image when mediaType.Contains("jpeg", StringComparison.OrdinalIgnoreCase) => ".jpg",
        ContentKind.Image when mediaType.Contains("png", StringComparison.OrdinalIgnoreCase) => ".png",
        ContentKind.Image when mediaType.Contains("gif", StringComparison.OrdinalIgnoreCase) => ".gif",
        ContentKind.Image => ".img",
        ContentKind.Video when mediaType.Contains("mp4", StringComparison.OrdinalIgnoreCase) => ".mp4",
        ContentKind.Video => ".video",
        ContentKind.Pdf => ".pdf",
        _ => ".bin"
    };
}
