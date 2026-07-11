using System.Text.Json;
using BrightCrawler.Core.Content;
using BrightCrawler.Infrastructure.Content;
using Xunit;

namespace BrightCrawler.IntegrationTests;

public sealed class ContentProcessorTests
{
    [Fact]
    public async Task ImageProcessor_ExtractsPngDimensions()
    {
        var processor = new ImageContentProcessor();
        var result = await processor.ProcessAsync(
            new ContentInput
            {
                CanonicalUrl = "https://example.com/pixel.png",
                MediaType = "image/png",
                Body = TestContent.OnePixelPng
            },
            CancellationToken.None);

        using var doc = JsonDocument.Parse(result.MetadataJson);
        Assert.Equal(1, doc.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("height").GetInt32());
    }
}
