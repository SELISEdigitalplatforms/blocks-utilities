namespace Subscription.DomainService.Enums;

/// <summary>How far a usage period's overage invoice has been carried toward being charged.</summary>
public enum SubscriptionUsageInvoiceState
{
    /// <summary>Priced, not yet charged — or a retry is still due.</summary>
    Pending = 0,

    /// <summary>Charged successfully. Terminal.</summary>
    Charged = 1,

    /// <summary>The period had no overage; nothing was ever owed. Terminal.</summary>
    NoCharge = 2,

    /// <summary>Every retry was spent without success. Terminal.</summary>
    Abandoned = 3
}
