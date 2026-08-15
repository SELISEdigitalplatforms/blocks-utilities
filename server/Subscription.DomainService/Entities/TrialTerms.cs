using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The trial this subscription started on, if any.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class TrialTerms
{
    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    /// <summary>
    /// Whether a card was taken up front. When false the subscription starts without any
    /// charge at all, because a zero-amount payment is not something the money path accepts.
    /// </summary>
    public bool RequiresPaymentMethod { get; set; } = true;

    /// <summary>Per-meter allowances for the trial, capped independently of the plan's own.</summary>
    public List<TrialMeterGrant> Grants { get; set; } = [];
}
