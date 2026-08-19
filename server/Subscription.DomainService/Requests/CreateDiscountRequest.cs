using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Requests;

public sealed class CreateDiscountRequest
{
    public string? OrganizationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DiscountKind Kind { get; set; }
    public int? PercentBasisPoints { get; set; }
    public long? AmountMinor { get; set; }
    public string? CurrencyCode { get; set; }
    public int? DurationPeriods { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public List<string> ApplicablePlanCodes { get; set; } = [];
}
