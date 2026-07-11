namespace BrightCrawler.Core.Frontier;

public enum FrontierState
{
    Pending,
    Leased,
    RetryScheduled,
    Succeeded,
    Redirected,
    NotFound,
    Blocked,
    Unsupported,
    Rejected,
    InvalidContent,
    FailedPermanent
}
