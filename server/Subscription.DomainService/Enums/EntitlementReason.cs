namespace Subscription.DomainService.Enums;

/// <summary>
/// Why an entitlement decision came out the way it did.
/// </summary>
/// <remarks>
/// Carried on every decision and logged with it, so a support engineer answering "why was this
/// firm blocked" reads an answer instead of inferring one from an absent subscription.
/// </remarks>
public enum EntitlementReason
{
    Allowed = 0,

    /// <summary>The organization has no subscription at all.</summary>
    NoSubscription = 1,

    /// <summary>A subscription exists but its status grants nothing.</summary>
    SubscriptionNotActive = 2,

    /// <summary>The plan does not carry this entitlement key.</summary>
    NotInPlan = 3,

    /// <summary>Capped, and the cap is used up.</summary>
    LimitReached = 4
}
