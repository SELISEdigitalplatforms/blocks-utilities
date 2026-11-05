namespace Subscription.DomainService.Enums;

/// <summary>
/// How a subscription's money is collected. See spec 006.
/// </summary>
/// <remarks>
/// Zero is <see cref="AutomaticCharge"/> because it is what every subscription written before this
/// existed means: an absent field must deserialize to the value that changes nothing. Values are
/// explicit because they are persisted.
/// <para>
/// Set once, when the subscription is created, and never changed. Switching an organization
/// between the two is a cancel and a new subscription, so no renewal ever has to decide which
/// rules a period half-billed under the other one follows.
/// </para>
/// </remarks>
public enum SubscriptionCollectionMethod
{
    /// <summary>Charged to a stored card or through hosted checkout, with dunning on decline.</summary>
    AutomaticCharge = 0,

    /// <summary>
    /// Paid by bank transfer under a contract, outside Utilities. Never charged, never dunned;
    /// documents are issued as a record only.
    /// </summary>
    OfflineInvoice = 1
}
