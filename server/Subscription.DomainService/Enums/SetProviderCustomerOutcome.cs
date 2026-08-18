namespace Subscription.DomainService.Enums;

/// <summary>
/// What recording a billing account's provider identifiers actually did.
/// </summary>
/// <remarks>
/// Three answers rather than a bool because they call for three different reactions, and the
/// bool this replaced collapsed the two that matter into one the caller discarded.
/// </remarks>
public enum SetProviderCustomerOutcome
{
    /// <summary>The account already named this customer and this card. Nothing to do.</summary>
    Unchanged = 0,

    /// <summary>A blank account was filled in for the first time. The ordinary first charge.</summary>
    Recorded = 1,

    /// <summary>
    /// The account named a different customer and now names this one.
    /// </summary>
    /// <remarks>
    /// Expected after a shopper's provider identity genuinely moves, and worth a log line every
    /// time: it is also what a misrouted payment looks like, and the two are told apart by
    /// whether it keeps happening.
    /// </remarks>
    Repointed = 2,

    /// <summary>No such account. Renewals have nothing to charge.</summary>
    AccountMissing = 3
}
