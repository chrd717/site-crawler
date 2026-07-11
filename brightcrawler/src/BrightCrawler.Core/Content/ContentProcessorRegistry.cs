namespace BrightCrawler.Core.Content;

public sealed class ContentProcessorRegistry
{
    private readonly IReadOnlyList<IContentProcessor> _processors;

    public ContentProcessorRegistry(IEnumerable<IContentProcessor> processors)
    {
        _processors = processors.ToList();
    }

    public IContentProcessor? Resolve(string mediaType)
    {
        var normalized = NormalizeMediaType(mediaType);
        return _processors.FirstOrDefault(p => p.CanProcess(normalized));
    }

    public static string NormalizeMediaType(string mediaType)
    {
        var semicolon = mediaType.IndexOf(';');
        var baseType = semicolon >= 0 ? mediaType[..semicolon] : mediaType;
        return baseType.Trim().ToLowerInvariant();
    }
}
