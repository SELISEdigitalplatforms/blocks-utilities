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
}
