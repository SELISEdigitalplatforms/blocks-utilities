namespace Subscription.DomainService.Enums;

/// <summary>
/// A short window a meter may cap usage within, inside its own billing period.
/// </summary>
/// <remarks>
/// Its own enum rather than <see cref="BillingInterval"/>, and deliberately so. A meter can be
/// capped by the hour, which is not a length anything is ever billed at — offering it on the
/// billing enum would let somebody author a subscription that renews hourly. The two say different
/// things and are not interchangeable.
/// </remarks>
public enum UsageWindow
{
    Hour = 0,
    Day = 1,
    Week = 2
}

/// <summary>
/// What happens when a sub-limit is reached while the period still has allowance left.
/// </summary>
public enum MeterSubLimitBehaviour
{
    /// <summary>
    /// The usage is rejected, exactly as exceeding an allowance with no overage is.
    /// </summary>
    Refuse = 0,

    /// <summary>
    /// The usage is accepted and reported as over the sub-limit.
    /// </summary>
    /// <remarks>
    /// Named for what the caller does with the answer rather than for what this does. Nothing here
    /// can slow anybody down — it records usage and answers questions — so throttling is the
    /// consumer's to perform, on being told it has gone past the pace the plan sells. A gateway
    /// reading this drops to a cheaper model or waits; this module keeps counting.
    /// </remarks>
    Throttle = 1
}
