namespace Subscription.DomainService.Responses;

public sealed class DiscountResponse
{
    public string DiscountId { get; init; } = string.Empty;
    public string? OrganizationId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public int? PercentBasisPoints { get; init; }
    public long? AmountMinor { get; init; }
    public string? CurrencyCode { get; init; }
    public int? DurationPeriods { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public List<string> ApplicablePlanCodes { get; init; } = [];
    public List<string> ApplicablePriceIds { get; init; } = [];
    public string Status { get; init; } = string.Empty;

    /// <summary>Read back on every response so a subsequent edit can be sent as an expected value.</summary>
    public long Version { get; init; }

    public string CampaignKind { get; init; } = string.Empty;
    public string CampaignPrecedence { get; init; } = string.Empty;
    public DateOnly? ValidFromDate { get; init; }
    public DateOnly? ValidThroughDate { get; init; }
    public string? TimeZoneId { get; init; }
    public DateTime? RedeemableFromUtc { get; init; }
    public DateTime? RedeemableUntilUtc { get; init; }
    public bool OneUsePerOrganization { get; init; }
    public bool ApplyToOpeningStub { get; init; }
    public bool RequiresPaymentMethodUpfront { get; init; }
    public string? EntitlementOverrideKey { get; init; }
    public long? EntitlementOverrideLimit { get; init; }

    /// <summary>How this campaign's effective state reads today, for the catalogue list.</summary>
    /// <remarks>
    /// One of: Upcoming, Active, Expired, Archived. Standard, when the discount carries no
    /// campaign window — Active/Archived only, exactly as before this field existed.
    /// <para>
    /// Reserved and redeemed counts are deliberately not on this response yet: they read off the
    /// redemption ledger, which does not exist until the concurrency/reservation phase of this
    /// feature lands. Adding them here now, always zero, would tell a UI built against this API
    /// that a campaign has never been redeemed when the honest answer is "not tracked yet" --
    /// worse than the fields being absent.
    /// </para>
    /// </remarks>
    public string EffectiveState { get; init; } = string.Empty;
}
