namespace Subscription.DomainService.Enums;

/// <summary>Whether a meter starts over with each usage window or accumulates for the subscription lifetime.</summary>
public enum MeterResetPolicy
{
    /// <summary>Default behavior: a new counter is addressed for every usage period.</summary>
    Periodic = 0,

    /// <summary>One stable counter survives renewals and usage-window boundaries.</summary>
    Never = 1
}
