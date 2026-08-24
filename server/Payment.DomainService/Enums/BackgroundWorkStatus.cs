namespace Payment.DomainService.Enums;

/// <summary>Where a scheduled unit of payment background work has got to.</summary>
/// <remarks>
/// Deliberately the payment module's own type rather than a shared one. Subscription depends on
/// payment, so a type shared between them would have to live here — and hoisting a scheduling
/// concept into the payment module for another module's benefit is how the two come to deploy
/// together forever. See Scheduling/README.md for the extraction this leaves open.
/// </remarks>
public enum BackgroundWorkStatus
{
    /// <summary>Waiting to be claimed. Due when its next attempt instant has passed.</summary>
    Pending = 0,

    /// <summary>Claimed under a lease. Reclaimable once that lease expires.</summary>
    Processing = 1,

    /// <summary>Done. Retained for a configured window, then purged.</summary>
    Completed = 2,

    /// <summary>
    /// Given up on. Never purged automatically: money may be unfinished behind it, and a queue that
    /// quietly forgets those is worse than one that grows.
    /// </summary>
    DeadLetter = 3,

    /// <summary>Set aside by a person, for work that must never run again.</summary>
    Abandoned = 4
}
