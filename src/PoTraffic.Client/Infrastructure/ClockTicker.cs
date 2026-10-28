namespace PoTraffic.Client.Infrastructure;

/// <summary>
/// One shared once-a-second heartbeat for everything on screen that counts.
///
/// <para>
/// Every <c>LiveCountdown</c> used to own a <see cref="Timer"/>, so a page with three
/// countdowns ran three timers that each woke the renderer on their own phase. Now they
/// subscribe here: one timer, one wake-up per second, and every subscriber renders off
/// the same instant — two countdowns to the same target can no longer disagree by a tick.
/// </para>
///
/// <para>
/// The timer runs only while something is listening. It starts on the first
/// subscription and stops when the last one leaves, so a page with no countdown on it
/// pays nothing. Subscribers are called on the timer thread; each marshals onto its own
/// renderer with <c>InvokeAsync</c>, exactly as it did with its private timer.
/// </para>
/// </summary>
public sealed class ClockTicker : IDisposable
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();
    private Action? _tick;
    private Timer? _timer;

    /// <summary>
    /// Raised once per second while it has at least one subscriber. Unsubscribe in
    /// <c>Dispose</c> — a forgotten handler keeps the timer, and the component, alive.
    /// </summary>
    public event Action Tick
    {
        add
        {
            lock (_gate)
            {
                _tick += value;
                _timer ??= new Timer(_ => _tick?.Invoke(), null, Period, Period);
            }
        }
        remove
        {
            lock (_gate)
            {
                _tick -= value;
                if (_tick is null)
                {
                    _timer?.Dispose();
                    _timer = null;
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _tick = null;
        }
    }
}
