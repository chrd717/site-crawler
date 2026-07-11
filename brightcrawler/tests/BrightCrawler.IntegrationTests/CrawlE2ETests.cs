using BrightCrawler.Core.Frontier;
using BrightCrawler.Core.Runs;
using BrightCrawler.Infrastructure.Fetching;
using BrightCrawler.Infrastructure.Persistence;
using Xunit;

namespace BrightCrawler.IntegrationTests;

public sealed class CrawlE2ETests : IClassFixture<CrawlE2ETestHost>
{
    private readonly CrawlE2ETestHost _host;

    public CrawlE2ETests(CrawlE2ETestHost host) => _host = host;

    [Fact]
    public async Task CyclicGraph_AtoBtoCtoA_ProducesExactlyThreeLogicalUrls()
    {
        const string a = "https://example.com/";
        const string b = "https://example.com/b";
        const string c = "https://example.com/c";

        var (runId, outputDir, _) = await _host.RunCrawlAsync(
            a,
            fetch =>
            {
                fetch.Register(a, () => TestContent.Html(TestContent.HtmlPage("A", b)));
                fetch.Register(b, () => TestContent.Html(TestContent.HtmlPage("B", c)));
                fetch.Register(c, () => TestContent.Html(TestContent.HtmlPage("C", a)));
            });

        try
        {
            var total = await CrawlTestDb.CountUrlsAsync(_host.ConnectionString, runId.Value);
            var succeeded = await CrawlTestDb.CountUrlsAsync(
                _host.ConnectionString, runId.Value, "succeeded");
            var urls = await CrawlTestDb.GetCanonicalUrlsAsync(_host.ConnectionString, runId.Value);

            Assert.Equal(3, total);
            Assert.Equal(3, succeeded);
            Assert.Equal(
                new[] { a, b, c }.OrderBy(x => x, StringComparer.Ordinal),
                urls.OrderBy(x => x, StringComparer.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(outputDir);
        }
    }

    [Fact]
    public async Task DuplicateLinks_FromMultiplePages_DeduplicateFrontierIdentity()
    {
        const string a = "https://example.com/";
        const string b = "https://example.com/b";
        const string c = "https://example.com/c";

        var (runId, outputDir, _) = await _host.RunCrawlAsync(
            a,
            fetch =>
            {
                fetch.Register(a, () => TestContent.Html(TestContent.HtmlPage("A", b, c)));
                fetch.Register(b, () => TestContent.Html(TestContent.HtmlPage("B")));
                fetch.Register(c, () => TestContent.Html(TestContent.HtmlPage("C", b)));
            });

        try
        {
            var total = await CrawlTestDb.CountUrlsAsync(_host.ConnectionString, runId.Value);
            var bCount = (await CrawlTestDb.GetCanonicalUrlsAsync(_host.ConnectionString, runId.Value))
                .Count(url => url == b);

            Assert.Equal(3, total);
            Assert.Equal(1, bCount);
        }
        finally
        {
            TryDeleteDirectory(outputDir);
        }
    }

    [Fact]
    public async Task RateLimitedThenSuccess_SchedulesDurableRetryAndEventuallySucceeds()
    {
        const string url = "https://example.com/retry-me";
        var attempts = 0;

        var (runId, outputDir, _) = await _host.RunCrawlAsync(
            url,
            fetch =>
            {
                fetch.Register(url, () =>
                {
                    if (Interlocked.Increment(ref attempts) == 1)
                    {
                        return TestContent.RateLimited(retryAfterSeconds: 1);
                    }

                    return TestContent.Html(TestContent.HtmlPage("Recovered"));
                });
            },
            options: new CrawlOptions { MaxConcurrency = 1 });

        try
        {
            var state = await CrawlTestDb.CountUrlsAsync(
                _host.ConnectionString, runId.Value, "succeeded");
            Assert.Equal(1, state);

            var urlAttempts = await CrawlTestDb.GetAttemptsAsync(
                _host.ConnectionString, runId.Value, url);

            Assert.Equal(2, urlAttempts.Count);
            Assert.Equal("retry_scheduled", urlAttempts[0].Outcome);
            Assert.Equal(429, urlAttempts[0].HttpStatus);
            Assert.NotNull(urlAttempts[0].RetryAt);
            Assert.Equal("succeeded", urlAttempts[1].Outcome);
            Assert.Equal(2, attempts);
        }
        finally
        {
            TryDeleteDirectory(outputDir);
        }
    }

    [Fact]
    public async Task MisleadingExtension_UsesPdfProcessorByMimeType()
    {
        const string url = "https://example.com/report.jpg";

        var (runId, outputDir, _) = await _host.RunCrawlAsync(
            "https://example.com/",
            fetch =>
            {
                fetch.Register("https://example.com/", () =>
                    TestContent.Html(TestContent.HtmlPage("Index", url)));
                fetch.Register(url, () => TestContent.Pdf(url));
            });

        try
        {
            var artifactPath = await CrawlTestDb.GetArtifactPathAsync(
                _host.ConnectionString, runId.Value, url);

            Assert.NotNull(artifactPath);
            Assert.StartsWith("pdfs/", artifactPath, StringComparison.Ordinal);
            Assert.EndsWith(".pdf", artifactPath, StringComparison.Ordinal);

            var fullPath = Path.Combine(outputDir, artifactPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(fullPath));
        }
        finally
        {
            TryDeleteDirectory(outputDir);
        }
    }

    [Fact]
    public async Task HtmlSuccess_PersistsDiscoveriesInDatabase()
    {
        const string seed = "https://example.com/";
        const string child = "https://example.com/child";

        var (runId, outputDir, _) = await _host.RunCrawlAsync(
            seed,
            fetch =>
            {
                fetch.Register(seed, () => TestContent.Html(TestContent.HtmlPage("Seed", child)));
                fetch.Register(child, () => TestContent.Html(TestContent.HtmlPage("Child")));
            });

        try
        {
            var snapshot = await new PostgresCrawlFrontier(_host.ConnectionString)
                .GetSnapshotAsync(runId, CancellationToken.None);

            Assert.True(snapshot.IsComplete);
            Assert.Equal(2, snapshot.TerminalCount);

            var childState = await CrawlTestDb.CountUrlsAsync(
                _host.ConnectionString, runId.Value, "succeeded");
            Assert.Equal(2, childState);
        }
        finally
        {
            TryDeleteDirectory(outputDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp output.
        }
    }
}
