using System.Text.Json.Serialization;

namespace Subscription.DomainService.Simulation;

/// <summary>
/// The reasons a tester can script a simulated payment to fail for, in the vocabulary a caller
/// asks for rather than the narrower one the domain actually distinguishes.
/// </summary>
/// <remarks>
/// The real system only tells apart <c>ProviderRejected</c> (a decline, whatever its reason),
/// <c>Unavailable</c> (the provider could not be reached) and <c>Timeout</c> (an answer never
/// came, and the charge may or may not have gone through). <see cref="Declined"/>,
/// <see cref="InsufficientFunds"/> and <see cref="PaymentMethodExpired"/> all map to the first —
/// they are distinguished here only so a test can name the scenario it means, and the caller's
/// own <c>errorCode</c> carries the distinction into the audit trail even though the dunning
/// logic itself does not branch on it.
/// <para>
/// Explicit string conversion — see the remark on <see cref="SimulatedRenewalOutcome"/> for why
/// a request-bound enum needs this even though this API's response enums do not.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SimulatedPaymentFailureKind
{
    Declined,
    InsufficientFunds,
    PaymentMethodExpired,
    ProviderUnavailable,

    /// <summary>
    /// The provider may have already taken the money; nothing here says either way. Mirrors the
    /// exact situation a real timeout leaves a renewal in, and — for an initial charge — the
    /// window <see cref="Outbox.ISubscriptionActivationProcessor.RecoverStaleAsync"/> exists to
    /// resolve.
    /// </summary>
    OutcomeUnknown
}
