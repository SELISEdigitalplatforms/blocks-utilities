namespace Subscription.DomainService.Enums;

/// <summary>
/// Why a payment was raised for a subscription.
/// </summary>
public enum SubscriptionPaymentPurpose
{
    /// <summary>The first charge, which activates the subscription.</summary>
    InitialCharge = 0,

    /// <summary>A period renewal. Reachable once a billing clock exists.</summary>
    Renewal = 1
}
