using System.Text.Json.Serialization;

namespace Subscription.DomainService.Simulation;

/// <summary>
/// What the one gateway call an advanced renewal makes should report — success folded in
/// alongside the same failure vocabulary <see cref="SimulatedPaymentFailureKind"/> uses, since
/// this single request field has to name either.
/// </summary>
/// <remarks>
/// Explicit string conversion: unlike this API's response DTOs, which hand-convert an enum to
/// <c>string</c> before it ever reaches <c>System.Text.Json</c>, this type is bound directly from
/// a request body, where the default numeric-value handling would otherwise reject the very
/// string names a caller of this JSON API would naturally send.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SimulatedRenewalOutcome
{
    Succeeded,
    Declined,
    InsufficientFunds,
    PaymentMethodExpired,
    ProviderUnavailable,
    OutcomeUnknown
}
