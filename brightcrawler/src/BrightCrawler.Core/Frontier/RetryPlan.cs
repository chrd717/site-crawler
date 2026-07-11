namespace BrightCrawler.Core.Frontier;

public sealed record RetryPlan
{
    public required int HttpStatus { get; init; }
    public required string ErrorCode { get; init; }
    public required string ErrorMessage { get; init; }
    public required DateTimeOffset AvailableAt { get; init; }
    public string? RetryAfterRaw { get; init; }
}
