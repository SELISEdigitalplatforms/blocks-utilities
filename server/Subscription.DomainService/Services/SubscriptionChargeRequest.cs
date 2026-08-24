namespace Subscription.DomainService.Services;

/// <summary>
/// What a renewal or dunning retry charges. Provider-neutral: <see cref="ProviderName"/> is
/// whatever the subscription's <c>BillingAccount</c> names, not a hardcoded gateway.
/// </summary>
public sealed class SubscriptionChargeRequest
{
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// The merchant's scope — whose provider configuration and saved card settle this.
    /// </summary>
    /// <remarks>
    /// Rarely the organization being billed. A tenant configures one provider and every
    /// organization's subscription is charged through it, so this is the scope that holds the
    /// card, while <see cref="SubscriberOrganizationId"/> is who the money is for.
    /// </remarks>
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>
    /// The organization whose subscription this pays for, recorded so the revenue can be
    /// attributed. Null leaves the payment attributed to the merchant scope alone.
    /// </summary>
    public string? SubscriberOrganizationId { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string StoredPaymentMethodId { get; set; } = string.Empty;

    /// <summary>The provider's customer id, for gateways that need one (e.g. a Stripe Invoice).</summary>
    public string? ProviderCustomerId { get; set; }

    /// <summary>Minor units — converted to a decimal only inside the gateway implementation.</summary>
    public long AmountMinor { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public string OrderId { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// The tax inside <see cref="AmountMinor"/>, already calculated by this module.
    /// </summary>
    /// <remarks>
    /// Passed rather than left for the provider to work out. This module is authoritative about tax:
    /// it knows the price's rate and whether the configured amount included it, and no provider-side
    /// tax feature is enabled. A gateway may only <em>show</em> this split — an invoice whose lines
    /// do not add up to what was charged is worse than one that shows a single line.
    /// </remarks>
    public long TaxAmountMinor { get; set; }

    /// <summary>What was taxed. Equals <see cref="AmountMinor"/> when there is no tax.</summary>
    public long NetAmountMinor { get; set; }

    /// <summary>Basis points, for the invoice line's own description. Null when untaxed.</summary>
    public int? TaxRateBasisPoints { get; set; }
}
