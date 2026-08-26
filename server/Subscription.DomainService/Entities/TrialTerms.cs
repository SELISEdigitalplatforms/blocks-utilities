using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The trial this subscription started on, if any.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class TrialTerms
{
    public DateTime StartsAtUtc { get; set; }

    /// <summary>
    /// Resolved once, at creation, from <see cref="DurationKind"/>/<see cref="DurationCount"/>
    /// in the subscription's own time zone — and never recomputed. A later catalogue edit to the
    /// plan's trial rule must not move an existing subscriber's boundary.
    /// </summary>
    public DateTime EndsAtUtc { get; set; }

    /// <summary>
    /// The rule <see cref="EndsAtUtc"/> was resolved from, kept for display and audit — not for
    /// recomputation, since <see cref="EndsAtUtc"/> is already the frozen answer.
    /// </summary>
    public TrialDurationKind DurationKind { get; set; } = TrialDurationKind.Days;

    /// <summary>The count <see cref="DurationKind"/> was measured with. Null for <see cref="TrialDurationKind.EndOfCalendarMonth"/>.</summary>
    public int? DurationCount { get; set; }

    /// <summary>
    /// Whether a card was taken up front. When false the subscription starts without any
    /// charge at all, because a zero-amount payment is not something the money path accepts.
    /// </summary>
    public bool RequiresPaymentMethod { get; set; } = true;

    /// <summary>Per-meter allowances for the trial, capped independently of the plan's own.</summary>
    public List<TrialMeterGrant> Grants { get; set; } = [];
}
