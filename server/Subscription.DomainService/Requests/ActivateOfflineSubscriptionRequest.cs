using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Requests;

/// <summary>
/// An operator activating a paid plan for an organization that pays by bank transfer. See spec 006.
/// </summary>
/// <remarks>
/// The same terms a card subscriber picks — plan, price, quantities, time zone, discount code —
/// plus how the recurring amount is priced. Nothing about a card, a provider or a return URL: an
/// offline subscription never reaches one.
/// </remarks>
public sealed class ActivateOfflineSubscriptionRequest
{
    /// <summary>The customer organization. Required: an operator always acts for somebody else.</summary>
    public string OrganizationId { get; set; } = string.Empty;

    public string PlanCode { get; set; } = string.Empty;

    /// <summary>The catalogue price whose interval, alignment and tax the subscription follows.</summary>
    public string PriceId { get; set; } = string.Empty;

    public IReadOnlyList<SubscriptionQuantityRequest> Quantities { get; init; } = [];

    /// <summary>The customer's own time zone, as an IANA identifier.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    public string? DiscountCode { get; set; }

    /// <summary>The language the billing contact's documents are mailed in. Absent means the profile's.</summary>
    public string? BillingLanguage { get; set; }

    public OfflinePricingRequest Pricing { get; set; } = new();

    /// <summary>
    /// The card-subscriber request these terms correspond to, so validation, pricing and the
    /// schedule are decided by exactly the code that decides them for everyone else.
    /// </summary>
    /// <remarks>
    /// Billing name and email are left unset on purpose: the contact then comes from the
    /// organization's billing profile, never from the operator's own token.
    /// </remarks>
    public CreateSubscriptionRequest ToCreateRequest() => new()
    {
        OrganizationId = OrganizationId,
        PlanCode = PlanCode,
        PriceId = PriceId,
        Quantities = [.. Quantities],
        TimeZoneId = TimeZoneId,
        DiscountCode = DiscountCode,
        BillingLanguage = BillingLanguage
    };
}

/// <summary>How an offline subscription's recurring amount is priced.</summary>
public sealed class OfflinePricingRequest
{
    public OfflinePricingMode Mode { get; set; } = OfflinePricingMode.Catalogue;

    /// <summary>Required for <see cref="OfflinePricingMode.Contract"/>: flat net amount per interval, in minor units.</summary>
    public long? ContractAmountMinor { get; set; }

    /// <summary>Required for <see cref="OfflinePricingMode.Contract"/>.</summary>
    public string? ContractCurrencyCode { get; set; }
}
