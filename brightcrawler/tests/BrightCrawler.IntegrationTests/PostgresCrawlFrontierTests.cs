using BrightCrawler.Core.Frontier;
using BrightCrawler.Core.Policies;
using BrightCrawler.Core.Runs;
using BrightCrawler.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace BrightCrawler.IntegrationTests;

public class PostgresCrawlFrontierTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        var initializer = new DatabaseInitializer(_connectionString);
        await initializer.InitializeAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task GetRunAsync_RoundTripsStoredOptionsAndCounters()
    {
        var frontier = new PostgresCrawlFrontier(_connectionString);
        var options = new CrawlOptions
        {
            MaxConcurrency = 2,
            MaxUrls = 42,
            OutputRoot = "custom-output"
        };
        var runId = await frontier.CreateRunAsync(new CrawlRunDefinition
        {
            SeedUrl = "https://example.com/",
            EffectiveHost = "example.com",
            Options = options
        }, CancellationToken.None);

        var info = await frontier.GetRunAsync(runId, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal(CrawlRunState.Running, info.State);
        Assert.Equal("https://example.com/", info.Definition.SeedUrl);
        Assert.Equal(2, info.Definition.Options.MaxConcurrency);
        Assert.Equal(42, info.Definition.Options.MaxUrls);
        Assert.Equal("custom-output", info.Definition.Options.OutputRoot);
        Assert.Equal(0, info.KnownUrlCount);
        Assert.Equal(0, info.DownloadedBytes);
    }

    [Fact]
    public async Task PrepareResumeAsync_ReopensPausedRun()
    {
        var frontier = new PostgresCrawlFrontier(_connectionString);
        var runId = await frontier.CreateRunAsync(new CrawlRunDefinition
        {
            SeedUrl = "https://example.com/",
            EffectiveHost = "example.com",
            Options = new CrawlOptions()
        }, CancellationToken.None);

        await frontier.MarkRunCompletedAsync(runId, CrawlRunState.Paused, "user cancel", CancellationToken.None);

        Assert.True(await frontier.PrepareResumeAsync(runId, CancellationToken.None));

        var info = await frontier.GetRunAsync(runId, CancellationToken.None);
        Assert.Equal(CrawlRunState.Running, info!.State);
    }

    [Fact]
    public async Task ConcurrentLeasesReturnDistinctEntries()
    {
        var frontier = new PostgresCrawlFrontier(_connectionString);
        var definition = new CrawlRunDefinition
        {
            SeedUrl = "https://example.com/",
            EffectiveHost = "example.com",
            Options = new CrawlOptions { LeaseDuration = TimeSpan.FromMinutes(1) }
        };

        var runId = await frontier.CreateRunAsync(definition, CancellationToken.None);

        await frontier.CompleteSuccessAsync(
            await frontier.TryLeaseNextAsync(runId, "w1", TimeSpan.FromMinutes(1), CancellationToken.None)
            ?? throw new InvalidOperationException("Expected lease"),
            new CrawlCompletion
            {
                HttpStatus = 200,
                MediaType = "text/html",
                ActualLength = 10,
                ContentSha256 = new byte[32],
                ArtifactPath = "html/ab/test.html",
                MetadataJson = "{}"
            },
            [
                new UrlDiscovery
                {
                    CanonicalUrl = "https://example.com/a",
                    CanonicalUrlHash = UrlCanonicalizer.TryCanonicalize("https://example.com/a", out _, out var h1) ? h1 : [],
                    FirstSeenUrl = "https://example.com/a",
                    Depth = 1,
                    RelationKind = "a[href]",
                    RawReference = "/a"
                },
                new UrlDiscovery
                {
                    CanonicalUrl = "https://example.com/b",
                    CanonicalUrlHash = UrlCanonicalizer.TryCanonicalize("https://example.com/b", out _, out var h2) ? h2 : [],
                    FirstSeenUrl = "https://example.com/b",
                    Depth = 1,
                    RelationKind = "a[href]",
                    RawReference = "/b"
                }
            ],
            CancellationToken.None);

        var leaseTasks = Enumerable.Range(0, 4)
            .Select(i => frontier.TryLeaseNextAsync(runId, $"worker-{i}", TimeSpan.FromMinutes(1), CancellationToken.None));
        var leases = await Task.WhenAll(leaseTasks);
        var active = leases.Where(l => l is not null).Select(l => l!.EntryId.Value).Distinct().ToList();

        Assert.Equal(2, active.Count);
    }

    [Fact]
    public async Task StaleLeaseTokenIsRejectedOnFinalize()
    {
        var frontier = new PostgresCrawlFrontier(_connectionString);
        var runId = await frontier.CreateRunAsync(new CrawlRunDefinition
        {
            SeedUrl = "https://example.com/",
            EffectiveHost = "example.com",
            Options = new CrawlOptions { LeaseDuration = TimeSpan.FromMilliseconds(1) }
        }, CancellationToken.None);

        var firstLease = await frontier.TryLeaseNextAsync(
            runId, "w1", TimeSpan.FromMilliseconds(1), CancellationToken.None);
        Assert.NotNull(firstLease);

        await Task.Delay(50);

        var secondLease = await frontier.TryLeaseNextAsync(
            runId, "w2", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(secondLease);

        var staleCommitted = await frontier.CompleteTerminalAsync(firstLease!, new TerminalOutcome
        {
            State = FrontierState.NotFound,
            ErrorCode = "stale",
            ErrorMessage = "should fail"
        }, CancellationToken.None);

        Assert.False(staleCommitted);
    }
}
