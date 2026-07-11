namespace BrightCrawler.Core.Policies;

public sealed class CrawlScope
{
    private readonly HashSet<string> _allowedHosts;

    public CrawlScope(string effectiveHost, IEnumerable<string>? additionalHosts = null)
    {
        _allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            effectiveHost.ToLowerInvariant()
        };

        if (additionalHosts is not null)
        {
            foreach (var host in additionalHosts)
            {
                _allowedHosts.Add(host.ToLowerInvariant());
            }
        }
    }

    public bool IsInScope(string canonicalUrl)
    {
        var host = UrlCanonicalizer.GetHost(canonicalUrl);
        return _allowedHosts.Contains(host);
    }
}
