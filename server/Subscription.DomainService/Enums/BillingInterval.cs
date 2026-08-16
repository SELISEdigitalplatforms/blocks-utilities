namespace Subscription.DomainService.Enums;

/// <summary>
/// The unit of a billing cadence. Paired with a count, so quarterly is three months and
/// fortnightly is two weeks — there is no member per marketable cadence.
/// </summary>
public enum BillingInterval
{
    Day = 0,
    Week = 1,
    Month = 2,
    Year = 3
}
