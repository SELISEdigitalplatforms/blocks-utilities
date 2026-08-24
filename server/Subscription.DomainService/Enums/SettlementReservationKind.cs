namespace Subscription.DomainService.Enums;

/// <summary>Which operation a settlement reservation is holding the subscription for.</summary>
public enum SettlementReservationKind
{
    /// <summary>More units, charged prorated for the rest of the paid period.</summary>
    QuantityIncrease = 0,

    /// <summary>A move to another plan or price, charged for the target period.</summary>
    PlanChange = 1
}
