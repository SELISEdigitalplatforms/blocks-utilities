namespace Subscription.DomainService.Enums;

/// <summary>
/// What a subscription charge was for, as read back from its order id.
/// </summary>
/// <remarks>
/// Not persisted anywhere — derived on the way out of a payment record, so the numbering carries no
/// compatibility obligation. What it names <em>is</em> persisted, in the order id itself.
/// </remarks>
public enum SubscriptionChargeKind
{
    /// <summary>Not a charge this module raised, or an order id it cannot read.</summary>
    Unknown = 0,

    /// <summary>The first charge, taken through hosted checkout before there was a period to name.</summary>
    Initial = 1,

    Renewal = 2,

    PlanChange = 3,

    QuantityChange = 4,

    /// <summary>Metered overage for a closed usage window.</summary>
    Usage = 5
}
