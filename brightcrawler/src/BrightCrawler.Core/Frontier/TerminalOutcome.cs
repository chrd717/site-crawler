namespace BrightCrawler.Core.Frontier;

public sealed record TerminalOutcome
{
    public required FrontierState State { get; init; }
    public int? HttpStatus { get; init; }
    public required string ErrorCode { get; init; }
    public required string ErrorMessage { get; init; }
    public string? MediaType { get; init; }
    public long? ActualLength { get; init; }
}
