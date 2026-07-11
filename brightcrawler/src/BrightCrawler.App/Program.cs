using BrightCrawler.Core.Crawling;
using BrightCrawler.Core.Frontier;
using BrightCrawler.Core.Policies;
using BrightCrawler.Core.Runs;
using BrightCrawler.Infrastructure;
using BrightCrawler.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BrightCrawler.App;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        using var host = BuildHost(args);
        var command = args[0].ToLowerInvariant();

        return command switch
        {
            "crawl" => await RunCrawlAsync(host.Services, args, cancellationToken: default),
            "resume" => await RunResumeAsync(host.Services, args, cancellationToken: default),
            "status" => await RunStatusAsync(host.Services, args),
            "init-db" => await InitDbAsync(host.Services),
            _ => PrintUsage()
        };
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine("""
            BrightCrawler commands:
              crawl <seed-url> [--workers N] [--output path] [--fetch-api url]
              resume <run-id> [--workers N] [--output path] [--fetch-api url]
              status <run-id>
              init-db
            """);
        return 1;
    }

    private static async Task<int> RunCrawlAsync(
        IServiceProvider services,
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length < 2)
        {
            return PrintUsage();
        }

        var seedUrl = args[1];
        if (!UrlCanonicalizer.TryCanonicalize(seedUrl, out _, out _))
        {
            Console.Error.WriteLine("Seed URL must be an absolute HTTP(S) URL.");
            return 1;
        }

        var baseOptions = services.GetRequiredService<CrawlOptions>();
        var options = baseOptions with
        {
            MaxConcurrency = ParseIntFlag(args, "--workers", baseOptions.MaxConcurrency),
            OutputRoot = ParseStringFlag(args, "--output", baseOptions.OutputRoot),
            FetchApiBaseUrl = ParseStringFlag(args, "--fetch-api", baseOptions.FetchApiBaseUrl)
        };

        var definition = new CrawlRunDefinition
        {
            SeedUrl = seedUrl,
            EffectiveHost = UrlCanonicalizer.GetHost(seedUrl),
            Options = options
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var initializer = services.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync(cts.Token);

        var coordinator = services.GetRequiredService<CrawlCoordinator>();
        var runId = await coordinator.RunAsync(definition, cts.Token);

        Console.WriteLine($"Crawl finished. Run id: {runId}");
        return 0;
    }

    private static async Task<int> RunResumeAsync(
        IServiceProvider services,
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length < 2 || !Guid.TryParse(args[1], out var runGuid))
        {
            Console.Error.WriteLine("Usage: resume <run-id> [--workers N] [--output path]");
            return 1;
        }

        var runId = new CrawlRunId(runGuid);
        var frontier = services.GetRequiredService<ICrawlFrontier>();
        var info = await frontier.GetRunAsync(runId, CancellationToken.None);
        if (info is null)
        {
            Console.Error.WriteLine($"Crawl run {runId} was not found.");
            return 1;
        }

        var baseOptions = info.Definition.Options;
        var options = baseOptions with
        {
            MaxConcurrency = ParseIntFlag(args, "--workers", baseOptions.MaxConcurrency),
            OutputRoot = ParseStringFlag(args, "--output", baseOptions.OutputRoot),
            FetchApiBaseUrl = ParseStringFlag(args, "--fetch-api", baseOptions.FetchApiBaseUrl)
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var initializer = services.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync(cts.Token);

        try
        {
            var coordinator = services.GetRequiredService<CrawlCoordinator>();
            var optionsOverride = options.Equals(baseOptions) ? null : options;
            await coordinator.ResumeAsync(runId, optionsOverride, cts.Token);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        Console.WriteLine($"Crawl resumed and finished. Run id: {runId}");
        return 0;
    }

    private static async Task<int> RunStatusAsync(IServiceProvider services, string[] args)
    {
        if (args.Length < 2 || !Guid.TryParse(args[1], out var runGuid))
        {
            Console.Error.WriteLine("Usage: status <run-id>");
            return 1;
        }

        var frontier = services.GetRequiredService<ICrawlFrontier>();
        var runId = new CrawlRunId(runGuid);
        var info = await frontier.GetRunAsync(runId, CancellationToken.None);
        if (info is null)
        {
            Console.Error.WriteLine($"Crawl run {runId} was not found.");
            return 1;
        }

        var snapshot = await frontier.GetSnapshotAsync(runId, CancellationToken.None);

        Console.WriteLine($"State: {info.State}");
        Console.WriteLine($"Seed: {info.Definition.SeedUrl}");
        Console.WriteLine($"Known URLs: {info.KnownUrlCount}");
        Console.WriteLine($"Downloaded bytes: {info.DownloadedBytes}");
        Console.WriteLine($"Pending: {snapshot.PendingCount}");
        Console.WriteLine($"Leased: {snapshot.LeasedCount}");
        Console.WriteLine($"Retry scheduled: {snapshot.RetryScheduledCount}");
        Console.WriteLine($"Terminal: {snapshot.TerminalCount}");
        if (snapshot.NextAvailableAt is not null)
        {
            Console.WriteLine($"Next retry at: {snapshot.NextAvailableAt:O}");
        }

        if (snapshot.OldestPendingAge is not null)
        {
            Console.WriteLine($"Oldest pending age: {snapshot.OldestPendingAge}");
        }

        Console.WriteLine($"Complete: {snapshot.IsComplete}");
        return 0;
    }

    private static async Task<int> InitDbAsync(IServiceProvider services)
    {
        var initializer = services.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        Console.WriteLine("Database schema applied.");
        return 0;
    }

    private static IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        var connectionString = builder.Configuration.GetConnectionString("CrawlDb")
            ?? "Host=localhost;Port=5432;Database=brightcrawler;Username=crawler;Password=crawler";

        var crawlSection = builder.Configuration.GetSection("Crawl");
        var options = new CrawlOptions
        {
            MaxConcurrency = crawlSection.GetValue("MaxConcurrency", 4),
            MaxDepth = crawlSection.GetValue("MaxDepth", 10),
            MaxUrls = crawlSection.GetValue("MaxUrls", 10_000),
            MaxBodyBytes = crawlSection.GetValue("MaxBodyBytes", 50L * 1024 * 1024),
            MaxTotalDownloadedBytes = crawlSection.GetValue("MaxTotalDownloadedBytes", 2L * 1024 * 1024 * 1024),
            MaxAttemptsPerUrl = crawlSection.GetValue("MaxAttemptsPerUrl", 5),
            RequestRatePerSecond = crawlSection.GetValue("RequestRatePerSecond", 10),
            RequestBurstSize = crawlSection.GetValue("RequestBurstSize", 20),
            FetchTimeout = TimeSpan.FromSeconds(crawlSection.GetValue("FetchTimeoutSeconds", 30)),
            ProcessingTimeout = TimeSpan.FromSeconds(crawlSection.GetValue("ProcessingTimeoutSeconds", 120)),
            LeaseDuration = TimeSpan.FromMinutes(crawlSection.GetValue("LeaseDurationMinutes", 5)),
            OutputRoot = crawlSection.GetValue("OutputRoot", "output") ?? "output",
            FetchApiBaseUrl = ParseStringFlag(
                args,
                "--fetch-api",
                crawlSection.GetValue("FetchApiBaseUrl", "http://mock-api.mock.com")
                    ?? "http://mock-api.mock.com")
        };

        builder.Services.AddBrightCrawlerInfrastructure(connectionString, options);
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });

        return builder.Build();
    }

    private static int ParseIntFlag(string[] args, string flag, int fallback)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value)
            ? value
            : fallback;
    }

    private static string ParseStringFlag(string[] args, string flag, string fallback)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
}
