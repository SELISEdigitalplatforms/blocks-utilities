namespace XUnitTest.Payment;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when a test moves it.
/// </summary>
/// <remarks>
/// Only <see cref="GetUtcNow"/> is overridden. Timers deliberately keep real system timing, so
/// a test that advances the clock past a cache lifetime does not also fast-forward the disposal
/// grace period — which is the property the eviction test needs to observe.
/// </remarks>
public sealed class ControlledTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public ControlledTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}
