using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Simulation;

/// <summary>
/// Wraps the real billing gateway with one escape hatch: when the harness is enabled and a
/// caller has scripted the next charge's outcome, that outcome is returned instead of placing a
/// real charge. Every other call — which is every call in production — passes straight through.
/// </summary>
/// <remarks>
/// Registered as the <see cref="ISubscriptionBillingGateway"/> DI entry in place of
/// <see cref="SubscriptionBillingGatewayResolver"/>, which this holds and delegates to. A caller
/// with nothing scripted, or a harness that is disabled, sees behaviour identical to the
/// resolver alone — the check is a config read and a dictionary lookup, not a code path only
/// present when testing.
/// </remarks>
public sealed class SubscriptionSimulationBillingGateway : ISubscriptionBillingGateway
{
    private readonly SubscriptionBillingGatewayResolver _real;
    private readonly ISubscriptionSimulatedOutcomeSource _scripted;
    private readonly IOptionsMonitor<SubscriptionSimulationOptions> _options;

    public SubscriptionSimulationBillingGateway(
        SubscriptionBillingGatewayResolver real,
        ISubscriptionSimulatedOutcomeSource scripted,
        IOptionsMonitor<SubscriptionSimulationOptions> options)
    {
        _real = real;
        _scripted = scripted;
        _options = options;
    }

    public Task<SubscriptionOperationResult<string>> ChargeAsync(
        SubscriptionChargeRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (_options.CurrentValue.Enabled && _scripted.TryConsume(out var scripted))
        {
            return Task.FromResult(scripted.Outcome switch
            {
                SimulatedChargeOutcome.Succeeded => SubscriptionOperationResult<string>.Success(
                    $"sim_pay_{Guid.NewGuid():N}", correlationId),
                SimulatedChargeOutcome.Rejected => SubscriptionOperationResult<string>.Failure(
                    PaymentFailureKind.ProviderRejected,
                    scripted.ErrorCode ?? "subscription_simulated_payment_declined",
                    scripted.ErrorMessage ?? "Simulated: the provider declined the charge.",
                    correlationId),
                SimulatedChargeOutcome.Unavailable => SubscriptionOperationResult<string>.Failure(
                    PaymentFailureKind.Unavailable,
                    scripted.ErrorCode ?? "subscription_simulated_payment_unavailable",
                    scripted.ErrorMessage ?? "Simulated: the payment provider was unreachable.",
                    correlationId),
                SimulatedChargeOutcome.TimedOut => SubscriptionOperationResult<string>.Failure(
                    PaymentFailureKind.Timeout,
                    scripted.ErrorCode ?? "subscription_simulated_payment_timeout",
                    scripted.ErrorMessage ??
                        "Simulated: no answer arrived — the charge may or may not have gone through.",
                    correlationId),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(scripted), scripted.Outcome, "Unrecognized simulated charge outcome.")
            });
        }

        return _real.ChargeAsync(request, idempotencyKey, correlationId, cancellationToken);
    }
}
