using Payment.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Picks which billing gateway charges a renewal, by provider name.
/// </summary>
/// <remarks>
/// The "Strategy + Resolver" pattern this module already uses for provider variation elsewhere —
/// a second provider gets a new implementation and a resolver entry, never a <c>switch</c> inside
/// <see cref="SubscriptionRenewalService"/>. Stripe gets real Invoice documents;
/// everything else — Adyen included — falls through to <see cref="RecurringChargeBillingGateway"/>
/// exactly as it did before this resolver existed, so adding Stripe Invoices changes nothing
/// about any other provider's behavior.
/// </remarks>
public sealed class SubscriptionBillingGatewayResolver : ISubscriptionBillingGateway
{
    private readonly StripeInvoiceBillingGateway _stripeInvoices;
    private readonly RecurringChargeBillingGateway _recurringCharge;

    public SubscriptionBillingGatewayResolver(
        StripeInvoiceBillingGateway stripeInvoices,
        RecurringChargeBillingGateway recurringCharge)
    {
        _stripeInvoices = stripeInvoices;
        _recurringCharge = recurringCharge;
    }

    public Task<SubscriptionOperationResult<string>> ChargeAsync(
        SubscriptionChargeRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var gateway = string.Equals(
            request.ProviderName,
            PaymentConstants.StripeProvider,
            StringComparison.OrdinalIgnoreCase)
            ? (ISubscriptionBillingGateway)_stripeInvoices
            : _recurringCharge;

        return gateway.ChargeAsync(request, idempotencyKey, correlationId, cancellationToken);
    }
}
