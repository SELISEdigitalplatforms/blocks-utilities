namespace Subscription.DomainService.Services;

/// <summary>
/// What a renewal or dunning retry charges. Provider-neutral: <see cref="ProviderName"/> is
/// whatever the subscription's <c>BillingAccount</c> names, not a hardcoded gateway.
/// </summary>
public sealed class SubscriptionChargeRequest
{
    public string ProviderName { get; set; } = string.Empty;

    public string StoredPaymentMethodId { get; set; } = string.Empty;

    /// <summary>Minor units — converted to a decimal only inside the gateway implementation.</summary>
    public long AmountMinor { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public string OrderId { get; set; } = string.Empty;

    public string? Description { get; set; }
}
