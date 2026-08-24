namespace XUnitTest.Payment;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when a test moves it.
/// </summary>
/// <remarks>
/// Only <see cref="GetUtcNow"/> is overridden. Timers deliberately keep real system timing, so
/// a test that advances the clock past a cache lifetime does not also fast-forward the disposal
/// grace period — which is the property the eviction test needs to observe.
/// <para>
/// The instant is held as ticks and read and written through <see cref="Volatile"/>, because tests
/// that exercise background work advance the clock from one thread while the code under test reads
/// it from another. Held in a plain field, that write is not guaranteed to be visible to the
/// reader — and a loop that keeps seeing a stale instant is a test that passes or fails by timing.
/// </para>
/// </remarks>
public sealed class ControlledTimeProvider : TimeProvider
{
    private long _utcTicks;

    public ControlledTimeProvider(DateTimeOffset utcNow)
    {
        _utcTicks = utcNow.UtcTicks;
    }

    public override DateTimeOffset GetUtcNow() =>
        new(Volatile.Read(ref _utcTicks), TimeSpan.Zero);

    public void Advance(TimeSpan delta) => Interlocked.Add(ref _utcTicks, delta.Ticks);
}
