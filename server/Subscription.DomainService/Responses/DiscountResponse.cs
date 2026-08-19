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
    public string Status { get; init; } = string.Empty;
}
