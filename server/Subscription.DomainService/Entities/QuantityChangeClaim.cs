using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// An increase in purchased quantity that has been reserved on the subscription and is waiting
/// for its charge to settle.
/// </summary>
/// <remarks>
/// Written <em>before</em> the card is charged, which is the whole point. Charging first and then
/// writing leaves the money moved and the units ungranted whenever the write loses a
/// compare-and-set, with no way back: the charge was keyed on the version the write just found
/// stale, so a retry raises a second charge rather than finding the first.
/// <para>
/// Claiming first inverts that. The one versioned write happens while nothing has been spent, the
/// charge is keyed on <see cref="ClaimId"/> — which no concurrent change can move — and the
/// promotion that grants the units is addressed by the same id rather than by a version. A
/// declined card releases the claim and the subscription stands exactly as it did.
/// </para>
/// <para>
/// One claim at a time. A second quantity change while one is in flight is refused rather than
/// queued: the second is being quoted against units the first has already reserved.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class QuantityChangeClaim
{
    /// <summary>
    /// What the charge is keyed on and what the promotion is addressed by. Stable for the life of
    /// the claim, so every retry of either finds the one attempt rather than starting another.
    /// </summary>
    public string ClaimId { get; set; } = string.Empty;

    public List<SubscriptionQuantityItem> RequestedQuantities { get; set; } = [];

    /// <summary>The prorated difference this claim is charging for.</summary>
    public long ChargeAmountMinor { get; set; }

    /// <summary>The credit balance the promotion writes, calculated with the charge.</summary>
    public long NewCreditBalanceMinor { get; set; }

    /// <summary>
    /// Where the charge was sent: the account, the provider, the customer and the card, exactly as
    /// the attempt used them.
    /// </summary>
    /// <remarks>
    /// Snapshotted because a replay has to repeat <em>that</em> attempt. Read from the billing
    /// account as it stands now instead, a replay could go to a different card, a different
    /// provider customer, or nowhere at all — and today's account saying there is no card is not
    /// evidence about what the provider did an hour ago. A card removed after the money moved would
    /// otherwise look exactly like a charge that never happened.
    /// </remarks>
    public string BillingAccountId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public string? ProviderOrganizationId { get; set; }

    public string? ProviderCustomerId { get; set; }

    public string StoredPaymentMethodId { get; set; } = string.Empty;

    public DateTime ClaimedAtUtc { get; set; }

    public string? RequestedByUserId { get; set; }

    /// <summary>Carried so a recovering sweep logs under the request that opened the claim.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// The version the claim was taken against, for the audit trail. Never used to address the
    /// promotion — a concurrent change moving the version must not strand a paid-for increase.
    /// </summary>
    public int ClaimedAtVersion { get; set; }
}
