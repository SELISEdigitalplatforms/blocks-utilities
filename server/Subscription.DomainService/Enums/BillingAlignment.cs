namespace Subscription.DomainService.Enums;

/// <summary>
/// Where a recurring price puts its renewal boundary.
/// </summary>
/// <remarks>
/// A cadence says how often; this says when. The two are independent — "every month" is the same
/// cadence whether it renews on the day the subscriber signed up or on the first of the month —
/// which is why this is its own concept rather than another <see cref="BillingInterval"/> member.
/// </remarks>
public enum BillingAlignment
{
    /// <summary>
    /// Renews on the anniversary of the signup. An August 25 signup renews September 25.
    /// </summary>
    /// <remarks>
    /// Zero, and therefore what every price and subscription authored before alignment existed
    /// deserializes to. That is deliberate: it is the behaviour they were sold on.
    /// </remarks>
    Anniversary = 0,

    /// <summary>
    /// Renews at local midnight on the first of the month, with a prorated first period covering
    /// the days from signup to that boundary.
    /// </summary>
    /// <remarks>
    /// Only meaningful for a price billed every single month. A cadence of three months has no
    /// "the first" to align to that is not also a choice of which month, and the plan builder
    /// refuses the combination rather than inventing one.
    /// </remarks>
    CalendarMonth = 1
}
