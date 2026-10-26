namespace Subscription.DomainService.Enums;

/// <summary>The unit a fixed credit lifetime is counted in. See <c>CreditExpiryDuration</c>.</summary>
public enum CreditExpiryUnit
{
    Hour = 0,
    Day = 1,
    Week = 2,
    Month = 3,
    Year = 4
}
