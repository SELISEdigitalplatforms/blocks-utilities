namespace Subscription.DomainService.Enums;

/// <summary>
/// How far a subscription's payment has been carried into the subscription's own state.
/// </summary>
public enum SubscriptionPaymentLinkState
{
    /// <summary>Raised, outcome not yet applied.</summary>
    Pending = 0,

    /// <summary>The outcome has been applied to the subscription. Terminal.</summary>
    Applied = 1,

    /// <summary>The payment failed or was never completed. Terminal.</summary>
    Abandoned = 2
}
