using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Requests;

/// <summary>
/// An edit to an existing discount. Everything <see cref="CreateDiscountRequest"/> accepts, plus
/// the version the caller last read.
/// </summary>
/// <remarks>
/// Code and scope are deliberately absent: both are immutable after creation, per the same rule
/// that makes a subscription's snapshot trustworthy -- a code that could be renamed out from under
/// an already-issued link, or a discount that could be re-scoped to a different organization after
/// subscribers have already redeemed it, would make every previous redemption's restriction mean
/// something different than it did when it was accepted.
/// </remarks>
public sealed class UpdateDiscountRequest : ICampaignDiscountRequest
{
    /// <summary>
    /// The <see cref="Entities.Discount.Version"/> this edit was read at. A mismatch against the
    /// stored value is refused with <c>subscription_discount_version_conflict</c> rather than
    /// applied over an edit the caller never saw.
    /// </summary>
    public long ExpectedVersion { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public DiscountKind Kind { get; set; }
    public int? PercentBasisPoints { get; set; }
    public long? AmountMinor { get; set; }
    public string? CurrencyCode { get; set; }
    public int? DurationPeriods { get; set; }
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public List<string> ApplicablePlanCodes { get; set; } = [];
    public List<string> ApplicablePriceIds { get; set; } = [];

    public CampaignKind CampaignKind { get; set; } = CampaignKind.Standard;
    public CampaignPrecedence CampaignPrecedence { get; set; } = CampaignPrecedence.BestDiscount;
    public DateOnly? ValidFromDate { get; set; }
    public DateOnly? ValidThroughDate { get; set; }
    public string? TimeZoneId { get; set; }
    public bool OneUsePerOrganization { get; set; }
    public bool RequiresPaymentMethodUpfront { get; set; }
    public string? EntitlementOverrideKey { get; set; }
    public long? EntitlementOverrideLimit { get; set; }
}
