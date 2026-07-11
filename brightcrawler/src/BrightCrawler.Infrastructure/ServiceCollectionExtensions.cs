using BrightCrawler.Core.Content;
using BrightCrawler.Core.Crawling;
using BrightCrawler.Core.Fetching;
using BrightCrawler.Core.Frontier;
using BrightCrawler.Core.RateControl;
using BrightCrawler.Core.Runs;
using BrightCrawler.Core.Storage;
using BrightCrawler.Infrastructure.Content;
using BrightCrawler.Infrastructure.Fetching;
using BrightCrawler.Infrastructure.Persistence;
using BrightCrawler.Infrastructure.RateControl;
using BrightCrawler.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BrightCrawler.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBrightCrawlerInfrastructure(
        this IServiceCollection services,
        string connectionString,
        CrawlOptions options,
        bool useInMemoryFetch = false)
    {
        services.AddSingleton(options);
        services.AddSingleton<ICrawlFrontier>(_ => new PostgresCrawlFrontier(connectionString));
        services.AddSingleton<DatabaseInitializer>(_ => new DatabaseInitializer(connectionString));
        services.AddSingleton<IArtifactStore>(_ => new FileSystemArtifactStore(options.OutputRoot));
        services.AddSingleton<IOutboundRequestGate>(_ => new OutboundRequestGate(
            options.MaxConcurrency,
            options.RequestRatePerSecond,
            options.RequestBurstSize));

        services.AddSingleton<ContentProcessorRegistry>(_ => new ContentProcessorRegistry([
            new HtmlContentProcessor(),
            new ImageContentProcessor(),
            new VideoContentProcessor(),
            new PdfContentProcessor()
        ]));

        if (useInMemoryFetch)
        {
            services.AddSingleton<InMemoryFetchApiClient>();
            services.AddSingleton<IFetchClient>(sp => sp.GetRequiredService<InMemoryFetchApiClient>());
        }
        else
        {
            services.AddHttpClient<IFetchClient, HttpFetchApiClient>(client =>
            {
                client.BaseAddress = new Uri(options.FetchApiBaseUrl.TrimEnd('/') + "/");
                client.Timeout = options.FetchTimeout;
            });
        }

        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<CrawlCoordinator>();

        services.AddSingleton<Func<CrawlRunContext, UrlProcessingPipeline>>(sp => context =>
            new UrlProcessingPipeline(
                sp.GetRequiredService<IFetchClient>(),
                sp.GetRequiredService<IArtifactStore>(),
                sp.GetRequiredService<IOutboundRequestGate>(),
                sp.GetRequiredService<ICrawlFrontier>(),
                sp.GetRequiredService<ContentProcessorRegistry>(),
                context.Scope,
                context.Definition.Options,
                context.Budget,
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<UrlProcessingPipeline>>()));

        return services;
    }
}
