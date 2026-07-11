namespace BrightCrawler.Core.RateControl;

public interface IOutboundRequestGate
{
    Task WaitAsync(CancellationToken cancellationToken);

    void Release();

    void ApplyCooldown(DateTimeOffset pauseUntil);

    DateTimeOffset PauseUntil { get; }
}
