using BrightCrawler.Core.Policies;
using Xunit;

namespace BrightCrawler.UnitTests;

public class UrlCanonicalizerTests
{
    [Theory]
    [InlineData("https://Example.com/path?q=1#frag", "https://example.com/path?q=1")]
    [InlineData("http://example.com:80/a", "http://example.com/a")]
    public void CanonicalizesConservatively(string input, string expected)
    {
        Assert.True(UrlCanonicalizer.TryCanonicalize(input, out var canonical, out var hash));
        Assert.Equal(expected, canonical);
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public void RejectsRelativeUrls()
    {
        Assert.False(UrlCanonicalizer.TryCanonicalize("/relative", out _, out _));
    }
}

public class CrawlScopeTests
{
    [Fact]
    public void AllowsSameHostDifferentScheme()
    {
        var scope = new CrawlScope("example.com");
        Assert.True(scope.IsInScope("https://example.com/a"));
        Assert.True(scope.IsInScope("http://example.com/b"));
        Assert.False(scope.IsInScope("https://cdn.example.com/c"));
    }
}

public class RetryPlannerTests
{
    [Fact]
    public void ParsesDeltaSecondsRetryAfter()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var result = new BrightCrawler.Core.Fetching.FetchResult
        {
            StatusCode = 429,
            Headers = new Dictionary<string, string> { ["Retry-After"] = "10" },
            Body = null
        };

        var retryAt = RetryPlanner.PlanRetry(time, 1, result, "10");
        Assert.True(retryAt >= time.GetUtcNow().AddSeconds(9));
    }
}

internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
