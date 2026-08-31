using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Requests;

/// <summary>
/// The fields <see cref="CreateDiscountRequest"/> and <see cref="UpdateDiscountRequest"/> share,
/// so campaign validation is written once and applied to both.
/// </summary>
/// <remarks>
/// Deliberately excludes <c>Code</c> and <c>OrganizationId</c>: both are immutable after creation,
/// so an update request never carries them and a shared rule set must not expect to find them.
/// </remarks>
public interface ICampaignDiscountRequest
{
    DiscountKind Kind { get; }
    int? PercentBasisPoints { get; }
    long? AmountMinor { get; }
    string? CurrencyCode { get; }
    DateTime? StartsAtUtc { get; }
    DateTime? ExpiresAtUtc { get; }
    List<string> ApplicablePlanCodes { get; }
    List<string> ApplicablePriceIds { get; }

    CampaignKind CampaignKind { get; }
    CampaignPrecedence? CampaignPrecedence { get; }
    DateOnly? ValidFromDate { get; }
    DateOnly? ValidThroughDate { get; }
    string? TimeZoneId { get; }
    bool OneUsePerOrganization { get; }
    bool RequiresPaymentMethodUpfront { get; }
    string? EntitlementOverrideKey { get; }
    long? EntitlementOverrideLimit { get; }
}
