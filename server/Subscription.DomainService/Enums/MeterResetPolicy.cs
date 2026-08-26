namespace Subscription.DomainService.Enums;

/// <summary>Whether a meter starts over with each usage window or accumulates for the subscription lifetime.</summary>
public enum MeterResetPolicy
{
    /// <summary>Default behavior: a new counter is addressed for every usage period.</summary>
    Periodic = 0,

    /// <summary>One stable counter survives renewals and usage-window boundaries.</summary>
    Never = 1,

    /// <summary>
    /// A new counter each window, opened with whatever the window before it did not use.
    /// </summary>
    /// <remarks>
    /// Addressed exactly like <see cref="Periodic"/> — same counter per window, same rating, same
    /// thresholds. Only the allowance the window opens with differs: the plan's included quantity
    /// plus the unused remainder of the previous window, bounded by
    /// <see cref="Entities.PlanMeter.CarryForwardCap"/>.
    /// <para>
    /// Distinct from <see cref="Never"/>, which holds one balance and one allowance for good. This
    /// one still resets, rates and reports per window; it simply does not discard what the
    /// customer paid for and did not consume.
    /// </para>
    /// </remarks>
    CarryForward = 2
}
