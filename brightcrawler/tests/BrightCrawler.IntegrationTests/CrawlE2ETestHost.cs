using BrightCrawler.Core.Crawling;
using BrightCrawler.Core.Runs;
using BrightCrawler.Infrastructure;
using BrightCrawler.Infrastructure.Fetching;
using BrightCrawler.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace BrightCrawler.IntegrationTests;

public sealed class CrawlE2ETestHost : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string _connectionString = string.Empty;

    public string ConnectionString => _connectionString;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await new DatabaseInitializer(_connectionString).InitializeAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    public async Task<(CrawlRunId RunId, string OutputDir, InMemoryFetchApiClient Fetch)> RunCrawlAsync(
        string seedUrl,
        Action<InMemoryFetchApiClient> configureFetch,
        CrawlOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "brightcrawler-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);

        options ??= new CrawlOptions();
        options = options with
        {
            OutputRoot = outputDir,
            MaxConcurrency = 1,
            RequestRatePerSecond = 100,
            RequestBurstSize = 100,
            LeaseDuration = TimeSpan.FromMinutes(1)
        };

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddBrightCrawlerInfrastructure(_connectionString, options, useInMemoryFetch: true);

        await using var provider = services.BuildServiceProvider();
        var fetch = provider.GetRequiredService<InMemoryFetchApiClient>();
        configureFetch(fetch);

        var coordinator = provider.GetRequiredService<CrawlCoordinator>();
        var definition = new CrawlRunDefinition
        {
            SeedUrl = seedUrl,
            EffectiveHost = new Uri(seedUrl).IdnHost.ToLowerInvariant(),
            Options = options
        };

        var runId = await coordinator.RunAsync(definition, cancellationToken);
        return (runId, outputDir, fetch);
    }

    public async Task<CrawlRunId> ResumeCrawlAsync(
        CrawlRunId runId,
        Action<InMemoryFetchApiClient> configureFetch,
        CancellationToken cancellationToken = default)
    {
        var frontier = new PostgresCrawlFrontier(_connectionString);
        var info = await frontier.GetRunAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException($"Run {runId} was not found.");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddBrightCrawlerInfrastructure(_connectionString, info.Definition.Options, useInMemoryFetch: true);

        await using var provider = services.BuildServiceProvider();
        var fetch = provider.GetRequiredService<InMemoryFetchApiClient>();
        configureFetch(fetch);

        var coordinator = provider.GetRequiredService<CrawlCoordinator>();
        return await coordinator.ResumeAsync(runId, optionsOverride: null, cancellationToken);
    }
}
