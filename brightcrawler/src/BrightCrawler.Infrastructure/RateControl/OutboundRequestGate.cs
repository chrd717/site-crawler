using System.Threading.RateLimiting;
using BrightCrawler.Core.RateControl;

namespace BrightCrawler.Infrastructure.RateControl;

public sealed class OutboundRequestGate : IOutboundRequestGate
{
    private readonly SemaphoreSlim _inFlight;
    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly object _pauseLock = new();
    private DateTimeOffset _pauseUntil = DateTimeOffset.MinValue;

    public OutboundRequestGate(int maxInFlight, int requestsPerSecond, int burstSize)
    {
        _inFlight = new SemaphoreSlim(maxInFlight, maxInFlight);
        _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = burstSize,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = int.MaxValue,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = requestsPerSecond,
            AutoReplenishment = true
        });
    }

    public DateTimeOffset PauseUntil
    {
        get
        {
            lock (_pauseLock)
            {
                return _pauseUntil;
            }
        }
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var pauseUntil = PauseUntil;
            if (pauseUntil > DateTimeOffset.UtcNow)
            {
                var delay = pauseUntil - DateTimeOffset.UtcNow;
                await Task.Delay(delay, cancellationToken);
            }

            using var lease = await _rateLimiter.AcquireAsync(1, cancellationToken);
            if (!lease.IsAcquired)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                continue;
            }

            await _inFlight.WaitAsync(cancellationToken);
            return;
        }
    }

    public void Release() => _inFlight.Release();

    public void ApplyCooldown(DateTimeOffset pauseUntil)
    {
        lock (_pauseLock)
        {
            if (pauseUntil > _pauseUntil)
            {
                _pauseUntil = pauseUntil;
            }
        }
    }
}
