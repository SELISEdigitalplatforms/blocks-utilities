using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Requests;

public sealed class CreateDiscountRequest : ICampaignDiscountRequest
{
    public string? OrganizationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DiscountKind Kind { get; set; }
    public int? PercentBasisPoints { get; set; }
    public long? AmountMinor { get; set; }
    public string? CurrencyCode { get; set; }
    public int? DurationPeriods { get; set; }
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public List<string> ApplicablePlanCodes { get; set; } = [];
    /// <summary>Empty is unrestricted by price. Narrows with the plan list, not instead of it.</summary>
    public List<string> ApplicablePriceIds { get; set; } = [];

    public CampaignKind CampaignKind { get; set; } = CampaignKind.Standard;
    public CampaignPrecedence CampaignPrecedence { get; set; } = CampaignPrecedence.BestDiscount;
    public DateOnly? ValidFromDate { get; set; }
    public DateOnly? ValidThroughDate { get; set; }

    /// <summary>
    /// The zone <see cref="ValidFromDate"/> and <see cref="ValidThroughDate"/> are read in.
    /// Required whenever either date is given; ignored for a <see cref="Enums.CampaignKind.Standard"/>
    /// request that names neither.
    /// </summary>
    public string? TimeZoneId { get; set; }

    public bool OneUsePerOrganization { get; set; }
    public bool RequiresPaymentMethodUpfront { get; set; }
    public string? EntitlementOverrideKey { get; set; }
    public long? EntitlementOverrideLimit { get; set; }
}
