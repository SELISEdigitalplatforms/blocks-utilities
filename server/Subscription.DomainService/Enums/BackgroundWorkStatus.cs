namespace Subscription.DomainService.Enums;

/// <summary>Where a scheduled unit of background work has got to.</summary>
public enum BackgroundWorkStatus
{
    /// <summary>Waiting to be claimed. Due when its next attempt instant has passed.</summary>
    Pending = 0,

    /// <summary>Claimed under a lease. Reclaimable once that lease expires.</summary>
    Processing = 1,

    /// <summary>Done. Retained for a configured window, then purged.</summary>
    Completed = 2,

    /// <summary>
    /// Given up on, either permanently refused or out of attempts. Never purged automatically:
    /// something financial may be unfinished behind it, and a queue that quietly forgets those is
    /// worse than one that grows.
    /// </summary>
    DeadLetter = 3
}
