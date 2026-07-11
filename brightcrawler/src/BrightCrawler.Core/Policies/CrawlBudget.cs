using BrightCrawler.Core.Frontier;

namespace BrightCrawler.Core.Policies;

public sealed class CrawlBudget
{
    private readonly object _lock = new();
    private long _downloadedBytes;
    private int _knownUrls;

    public CrawlBudget(int maxUrls, long maxTotalDownloadedBytes, int maxDepth)
    {
        MaxUrls = maxUrls;
        MaxTotalDownloadedBytes = maxTotalDownloadedBytes;
        MaxDepth = maxDepth;
    }

    public int MaxUrls { get; }
    public long MaxTotalDownloadedBytes { get; }
    public int MaxDepth { get; }

    public bool CanDiscoverMore()
    {
        lock (_lock)
        {
            return _knownUrls < MaxUrls;
        }
    }

    public bool TryReserveUrlSlot()
    {
        lock (_lock)
        {
            if (_knownUrls >= MaxUrls)
            {
                return false;
            }

            _knownUrls++;
            return true;
        }
    }

    public bool TryReserveBytes(long bytes)
    {
        lock (_lock)
        {
            if (_downloadedBytes + bytes > MaxTotalDownloadedBytes)
            {
                return false;
            }

            _downloadedBytes += bytes;
            return true;
        }
    }

    public void SeedReserved()
    {
        lock (_lock)
        {
            _knownUrls = 1;
        }
    }
}
