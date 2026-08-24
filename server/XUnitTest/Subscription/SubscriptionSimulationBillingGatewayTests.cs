using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Simulation;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// <see cref="SubscriptionSimulationBillingGateway"/>'s own logic — mapping a scripted outcome
/// onto a gateway result. The pass-through branch (nothing scripted, or the harness disabled)
/// is not exercised here: the gateway it delegates to,
/// <see cref="Subscription.DomainService.Services.SubscriptionBillingGatewayResolver"/>, is
/// sealed and holds two further concrete gateways, so faking it would mean constructing the real
/// dependency chain rather than testing this class in isolation. That branch is a single
/// unconditional delegating call, visible by inspection.
/// </summary>
public sealed class SubscriptionSimulationBillingGatewayTests
{
    private readonly Mock<ISubscriptionSimulatedOutcomeSource> _scripted = new();
    private readonly Mock<IOptionsMonitor<SubscriptionSimulationOptions>> _options = new();

    public SubscriptionSimulationBillingGatewayTests() =>
        _options.Setup(o => o.CurrentValue).Returns(new SubscriptionSimulationOptions { Enabled = true });

    [Fact]
    public async Task Returns_the_scripted_success_without_reaching_the_real_gateway()
    {
        Scripts(new ScriptedChargeOutcome(SimulatedChargeOutcome.Succeeded, null, null));

        var result = await CreateGateway().ChargeAsync(
            request: null!, "key-1", "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(SimulatedChargeOutcome.Rejected, PaymentFailureKind.ProviderRejected)]
    [InlineData(SimulatedChargeOutcome.Unavailable, PaymentFailureKind.Unavailable)]
    [InlineData(SimulatedChargeOutcome.TimedOut, PaymentFailureKind.Timeout)]
    public async Task Maps_every_scripted_failure_to_its_own_failure_kind(
        SimulatedChargeOutcome scripted, PaymentFailureKind expected)
    {
        Scripts(new ScriptedChargeOutcome(scripted, "code", "message"));

        var result = await CreateGateway().ChargeAsync(
            request: null!, "key-1", "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(expected);
        result.ErrorCode.Should().Be("code");
    }

    [Fact]
    public async Task Does_not_consult_the_script_when_the_harness_is_disabled()
    {
        _options.Setup(o => o.CurrentValue).Returns(new SubscriptionSimulationOptions { Enabled = false });
        Scripts(new ScriptedChargeOutcome(SimulatedChargeOutcome.Succeeded, null, null));

        // With no real gateway to delegate to, this would throw a NullReferenceException if the
        // disabled harness still reached the passthrough — proving the guard runs first.
        var act = async () => await CreateGateway().ChargeAsync(
            request: null!, "key-1", "corr-1", CancellationToken.None);

        await act.Should().ThrowAsync<NullReferenceException>();
        _scripted.Verify(
            source => source.TryConsume(out It.Ref<ScriptedChargeOutcome>.IsAny), Times.Never);
    }

    private void Scripts(ScriptedChargeOutcome outcome) =>
        _scripted
            .Setup(source => source.TryConsume(out It.Ref<ScriptedChargeOutcome>.IsAny))
            .Returns(new TryConsumeCallback((out ScriptedChargeOutcome result) =>
            {
                result = outcome;
                return true;
            }));

    private SubscriptionSimulationBillingGateway CreateGateway() =>
        new(real: null!, _scripted.Object, _options.Object);

    private delegate bool TryConsumeCallback(out ScriptedChargeOutcome outcome);
}
