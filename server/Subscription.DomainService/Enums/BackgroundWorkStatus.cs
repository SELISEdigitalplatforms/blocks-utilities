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
    DeadLetter = 3,

    /// <summary>
    /// Set aside by a person, for work that should never run again.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="DeadLetter"/> on purpose. Dead-lettered means the system stopped
    /// trying; abandoned means somebody looked and decided it must not be tried. Collapsing the two
    /// would leave an operator unable to tell what still needs a decision from what has had one —
    /// which is the only question a dead-letter queue exists to answer.
    /// <para>
    /// Never purged either: the reason it was abandoned is part of the record.
    /// </para>
    /// </remarks>
    Abandoned = 4
}
