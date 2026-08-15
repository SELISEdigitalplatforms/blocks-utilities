namespace Subscription.DomainService.Enums;

/// <summary>
/// What kind of answer an entitlement gives. The platform never interprets the entitlement's
/// meaning — only whether it is on, capped, or uncapped.
/// </summary>
public enum EntitlementLimitKind
{
    /// <summary>On or off. No counting.</summary>
    Boolean = 0,

    /// <summary>Capped at a number, drawn down by a meter.</summary>
    Count = 1,

    /// <summary>Present and uncapped. Never reports a limit reached.</summary>
    Unlimited = 2
}
