namespace TrafficLimiter.Core;

/// <summary>
/// Token-Bucket mit "Schulden": Pakete werden nie abgelehnt, sondern bekommen eine Wartezeit.
/// reserve (0..1) hält einen Teil der Kapazität für höhere Prioritäten frei.
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
            _capacity = Math.Max(bytesPerSec * 0.25, 65536); // mind. ein Jumbo-Paket
            _tokens = Math.Min(_tokens, _capacity);
            _last = now;
        }
    }

    /// <summary>Bucket belasten; liefert Sekunden, die das Paket warten muss.</summary>
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
