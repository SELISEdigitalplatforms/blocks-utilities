namespace Subscription.DomainService.Enums;

/// <summary>
/// The whole lifecycle, defined now so later phases add transitions rather than columns.
/// </summary>
/// <remarks>
/// Phase 1 drives only <see cref="Incomplete"/> to <see cref="Trialing"/> or
/// <see cref="Active"/>, <see cref="Incomplete"/> to <see cref="IncompleteExpired"/>, and
/// cancellation. <see cref="PastDue"/> and <see cref="Unpaid"/> are reachable only once a
/// billing clock exists, but entitlement already answers correctly for them.
/// <para>
/// Values are explicit because they are persisted: inserting a member without one would
/// renumber every value after it and silently reinterpret stored documents.
/// </para>
/// </remarks>
public enum SubscriptionStatus
{
    /// <summary>Created, first charge not yet confirmed. Grants nothing.</summary>
    Incomplete = 0,

    /// <summary>The first charge never completed. Terminal.</summary>
    IncompleteExpired = 1,

    /// <summary>Entitled, nobody has paid yet. Distinct from active so reporting can tell them apart.</summary>
    Trialing = 2,

    /// <summary>Entitled and paid.</summary>
    Active = 3,

    /// <summary>A renewal failed and the provider is still retrying. Entitled during the grace period.</summary>
    PastDue = 4,

    /// <summary>Retries are exhausted. Not entitled, but not yet ended.</summary>
    Unpaid = 5,

    /// <summary>Ended, by request or by non-payment. Terminal.</summary>
    Canceled = 6
}
