using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BrightCrawler.Core.Policies;

public static class UrlCanonicalizer
{
    public static bool TryCanonicalize(string rawUrl, out string canonicalUrl, out byte[] hash)
    {
        canonicalUrl = string.Empty;
        hash = [];

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.IdnHost.ToLowerInvariant(),
            Fragment = string.Empty
        };

        if ((builder.Scheme == "http" && builder.Port == 80) ||
            (builder.Scheme == "https" && builder.Port == 443))
        {
            builder.Port = -1;
        }

        canonicalUrl = builder.Uri.GetComponents(
            UriComponents.AbsoluteUri & ~UriComponents.Fragment,
            UriFormat.UriEscaped);

        hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalUrl));
        return true;
    }

    public static string GetHost(string canonicalUrl)
    {
        var uri = new Uri(canonicalUrl);
        return uri.IdnHost.ToLowerInvariant();
    }
}
