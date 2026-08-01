using Payment.DomainService.Providers.HostedCheckout;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Projects the shared client outcome onto the per-operation outcome enums.
/// </summary>
/// <remarks>
/// Refunds and captures each carry their own enum, but the decision that produces them — is
/// this Stripe failure terminal or worth retrying — is identical.
/// Keeping the classification in <see cref="StripeOutcomeMapper"/> and the projection here
/// means a change to that judgement lands once rather than in every gateway.
/// </remarks>
public static class StripeProviderOutcome
{
    public static PaymentRefundProviderOutcome ToRefund(ProviderClientOutcome outcome) =>
        outcome switch
        {
            ProviderClientOutcome.Success => PaymentRefundProviderOutcome.Submitted,
            ProviderClientOutcome.Rejected => PaymentRefundProviderOutcome.Rejected,
            ProviderClientOutcome.Timeout => PaymentRefundProviderOutcome.Timeout,
            ProviderClientOutcome.Unavailable => PaymentRefundProviderOutcome.Unavailable,
            _ => PaymentRefundProviderOutcome.OutcomeUnknown
        };

    public static StoredPaymentChargeOutcome ToCharge(ProviderClientOutcome outcome) =>
        outcome switch
        {
            ProviderClientOutcome.Success => StoredPaymentChargeOutcome.Accepted,
            ProviderClientOutcome.Rejected => StoredPaymentChargeOutcome.Rejected,
            ProviderClientOutcome.Timeout => StoredPaymentChargeOutcome.Timeout,
            ProviderClientOutcome.Unavailable => StoredPaymentChargeOutcome.Unavailable,
            _ => StoredPaymentChargeOutcome.OutcomeUnknown
        };

    public static PaymentCaptureProviderOutcome ToCapture(ProviderClientOutcome outcome) =>
        outcome switch
        {
            ProviderClientOutcome.Success => PaymentCaptureProviderOutcome.Submitted,
            ProviderClientOutcome.Rejected => PaymentCaptureProviderOutcome.Rejected,
            ProviderClientOutcome.Timeout => PaymentCaptureProviderOutcome.Timeout,
            ProviderClientOutcome.Unavailable => PaymentCaptureProviderOutcome.Unavailable,
            _ => PaymentCaptureProviderOutcome.OutcomeUnknown
        };
}
