namespace TrafficLimiter.Core;

/// <summary>
/// Token bucket with "debt": packets are never rejected, they get a delay instead.
/// reserve (0..1) keeps part of the capacity free for higher priorities.
/// </summary>
public sealed class TokenBucket
{
    readonly object _lock = new();
    double _rate, _capacity, _tokens, _last;

    public TokenBucket(double bytesPerSec, double now) { SetRate(bytesPerSec, now); _tokens = _capacity; }

    public double Rate => _rate;

    public void SetRate(double bytesPerSec, double now)
    {
        lock (_lock)
        {
            _rate = bytesPerSec;
            _capacity = Math.Max(bytesPerSec * 0.25, 65536); // at least one jumbo packet
            _tokens = Math.Min(_tokens, _capacity);
            _last = now;
        }
    }

    /// <summary>Charges the bucket; returns the seconds the packet must wait.</summary>
    public double Delay(int bytes, double now, double reserve = 0)
    {
        lock (_lock)
        {
            _tokens = Math.Min(_capacity, _tokens + (now - _last) * _rate);
            _last = now;
            _tokens -= bytes;
            double need = reserve * _capacity - _tokens;
            return need <= 0 ? 0 : need / _rate;
        }
    }
}
