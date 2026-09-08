using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// The payment services read the clock they were given, not the machine's.
/// </summary>
/// <remarks>
/// Moving a service off <c>DateTime.UtcNow</c> compiles whether or not the injected clock is
/// actually consulted — a leftover static read behaves identically until the day something needs
/// to control time, and then fails silently by being right about the wrong instant. These tests
/// move a controlled clock and require the service to notice, which a static read cannot do.
/// <para>
/// Expiry is the property worth pinning: a checkout callback that outlives its window, or a
/// provider cache that serves a stale secret, are both failures that only appear under a clock
/// nobody can steer in a test.
/// </para>
/// </remarks>
public sealed class PaymentServiceClockTests
{
    private const string ActiveKey = "return-state-key-that-is-longer-than-thirty-two-bytes";

    private static readonly DateTimeOffset Noon =
        new(2026, 3, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_callback_state_is_issued_at_the_injected_clocks_instant()
    {
        var clock = new ControlledTimeProvider(Noon);
        var protector = new CheckoutCallbackStateProtector(clock);

        var protectedState = protector.Create(
            "tenant-a", null, "payment-1", "ADYEN-ONLINE", TimeSpan.FromMinutes(30), ActiveKey);

        protectedState.State.IssuedAtUtc.Should().Be(
            Noon.UtcDateTime,
            "the state is stamped from the injected clock, so a controlled clock decides it");
        protectedState.State.ExpiresAtUtc.Should().Be(Noon.UtcDateTime.AddMinutes(30));
    }

    [Fact]
    public void A_callback_state_stops_verifying_once_the_injected_clock_passes_its_expiry()
    {
        var clock = new ControlledTimeProvider(Noon);
        var protector = new CheckoutCallbackStateProtector(clock);
        var protectedState = protector.Create(
            "tenant-a", null, "payment-1", "ADYEN-ONLINE", TimeSpan.FromMinutes(30), ActiveKey);

        protector.TryUnprotect(protectedState.Token, ActiveKey, null, out _)
            .Should().BeTrue("the clock has not moved, so the state is still inside its window");

        clock.Advance(TimeSpan.FromMinutes(31));

        protector.TryUnprotect(protectedState.Token, ActiveKey, null, out _)
            .Should().BeFalse(
                "the window closed on the injected clock -- a static DateTime.UtcNow read here " +
                "would still accept the token, which is exactly the bug this guards");
    }

    /// <summary>
    /// The default keeps every existing caller — production hosts included — on the system clock.
    /// </summary>
    [Fact]
    public void A_protector_built_without_a_clock_still_works_on_the_system_clock()
    {
        var protector = new CheckoutCallbackStateProtector();

        var protectedState = protector.Create(
            "tenant-a", null, "payment-1", "ADYEN-ONLINE", TimeSpan.FromMinutes(30), ActiveKey);

        protector.TryUnprotect(protectedState.Token, ActiveKey, null, out _).Should().BeTrue();
        protectedState.State.IssuedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Registration has to supply the clock, or every service falls back to its own default and
    /// a host that substitutes a controlled clock silently does not get one.
    /// </summary>
    [Fact]
    public void The_payment_module_registers_a_clock()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.RegisterPaymentDomainServices(
            new ConfigurationBuilder().AddInMemoryCollection([]).Build());

        using var provider = services.BuildServiceProvider();

        provider.GetService<TimeProvider>()
            .Should().NotBeNull("the payment services take a TimeProvider and must be able to get one");
    }

    /// <summary>
    /// TryAdd, so the module cannot displace a clock the host or another module already chose.
    /// Both payment and subscription register the same production default.
    /// </summary>
    [Fact]
    public void Registration_does_not_replace_a_clock_the_host_already_chose()
    {
        var chosen = new ControlledTimeProvider(Noon);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(chosen);

        services.RegisterPaymentDomainServices(
            new ConfigurationBuilder().AddInMemoryCollection([]).Build());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(chosen);
    }
}
