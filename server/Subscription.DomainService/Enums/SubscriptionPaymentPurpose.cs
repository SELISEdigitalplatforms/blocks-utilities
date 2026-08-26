using System.Text.Json.Serialization;

namespace Subscription.DomainService.Enums;

/// <summary>
/// Why a payment was raised for a subscription.
/// </summary>
/// <remarks>
/// Explicit string conversion — see the remark on
/// <see cref="Subscription.DomainService.Simulation.SimulatedRenewalOutcome"/> for why a
/// request-bound enum needs this even though this API's response enums do not: this type is
/// bound directly from the simulation harness's request bodies.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubscriptionPaymentPurpose
{
    /// <summary>The first charge, which activates the subscription.</summary>
    InitialCharge = 0,

    /// <summary>A period renewal. Reachable once a billing clock exists.</summary>
    Renewal = 1,

    /// <summary>
    /// Collecting a card for a subscription that owes nothing today.
    /// </summary>
    /// <remarks>
    /// Linked and swept exactly as a charge is, because what activation waits for is the same
    /// thing either way: the provider confirming, over a signed channel, that what was asked for
    /// happened. What differs is the consequence of failure — no money was lost, so the
    /// subscription stays open for another attempt rather than expiring.
    /// </remarks>
    PaymentMethodSetup = 2
}
