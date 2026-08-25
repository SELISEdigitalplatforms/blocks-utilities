using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A financial event, recorded on the subscription so that the document it owes can always be
/// issued — and issued describing the terms that were in force when the event happened.
/// </summary>
/// <remarks>
/// Two problems, one record.
/// <para>
/// The first is <em>durability</em>. Scheduling a document is a write to another database with no
/// transaction shared with the money, so a crash in that window used to leave nothing behind but a
/// payment, and one kind of event — a change that banks credit rather than charging for it — left
/// nothing at all, because the credit is folded into a balance that cannot say which change put it
/// there. Appended in the same update as the state change it belongs to, this record makes the event
/// and its obligation atomic in the way <see cref="SubscriptionOutboxEvent"/> makes an event and its
/// publication atomic. The sweep then has something to find, with no time window, for as long as the
/// document is owed.
/// </para>
/// <para>
/// The second is <em>historical accuracy</em>. A document issued minutes or days late must describe
/// the plan, price, quantities and period as they were when the money moved, not as they are when the
/// document is finally written. Deriving them from the live subscription is only correct while nothing
/// has changed since, which is exactly the assumption a delayed or recovered issue breaks. The terms
/// are therefore frozen here, at the transition, and the issuer prefers them over anything it could
/// read today.
/// </para>
/// <para>
/// The amounts are deliberately <em>not</em> frozen for a charge: what was actually taken is on the
/// payment, which is the only record the bank agrees with. This freezes what the money was
/// <em>for</em>.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionDocumentSource
{
    /// <summary>
    /// The identity of the document this owes, where the event already knows it.
    /// </summary>
    /// <remarks>
    /// The same key the document will carry, derived the same way — from a payment id, a subscription
    /// and a trial instant, or a change reference. Derived rather than generated, so a replayed
    /// transition and the sweep both arrive at it.
    /// <para>
    /// Doubles as the deduplication key at both ends: appending is filtered on no source already
    /// carrying it, and the ledger holds a unique index on it.
    /// </para>
    /// </remarks>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>
    /// The billing or usage period the charge covered, for the kinds that have one.
    /// </summary>
    /// <remarks>
    /// Kept beside the resolved <see cref="Period"/> as the record of which period was asked for,
    /// separately from the dates that answer resolved to.
    /// </remarks>
    public string? PeriodKey { get; set; }

    /// <summary>The settled charge this describes, where there is one.</summary>
    public string? PaymentDetailId { get; set; }

    /// <summary>What identifies the change a credit note settles: a reservation, or a version.</summary>
    public string? SettlementReservationId { get; set; }

    public FinancialDocumentType DocumentType { get; set; }

    public SubscriptionChargeKind ChargeKind { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>What was being paid for, as the catalogue described it at the time.</summary>
    public FinancialDocumentSubject Subject { get; set; } = new();

    /// <summary>
    /// The units held when the event happened, which is what the document's lines describe.
    /// </summary>
    /// <remarks>
    /// Frozen beside the subject because the two are read together and move together: an invoice
    /// naming last month's plan and this month's seat count describes a purchase that never happened.
    /// </remarks>
    public List<SubscriptionQuantityItem> QuantityItems { get; set; } = [];

    /// <summary>
    /// The lines, for an event whose lines cannot be composed from the terms and the payment.
    /// </summary>
    /// <remarks>
    /// Set only for a credit note. A charge's lines depend on figures that live on the payment, so
    /// they are composed at issue from these frozen terms and those figures rather than guessed here.
    /// </remarks>
    public List<FinancialDocumentLine> Lines { get; set; } = [];

    public FinancialDocumentPeriod Period { get; set; } = new();

    public FinancialDocumentTrial? Trial { get; set; }

    /// <summary>
    /// The two sides of a change, frozen because a settlement is a subtraction and neither side of it
    /// survives on the subscription once the change is applied.
    /// </summary>
    public SubscriptionSettlementBreakdown? Settlement { get; set; }

    /// <summary>
    /// The figures, for an event that has no payment to read them from.
    /// </summary>
    /// <remarks>
    /// Set only for a credit note banked by a change. A charge leaves its figures on the payment,
    /// which is the authoritative record; copying them here would be a second version of the same
    /// numbers, free to disagree with the one the bank saw.
    /// </remarks>
    public FinancialDocumentAmounts? Amounts { get; set; }

    public long CreditedMinor { get; set; }

    /// <summary>
    /// Who acted, as their identity provider described them at the moment they acted.
    /// </summary>
    /// <remarks>
    /// Captured here rather than looked up at issue because it cannot be re-derived: the person may
    /// have left, been renamed, or never have had a billing contact recorded. A worker renewal
    /// carries no user and is named as the system it is.
    /// </remarks>
    public FinancialDocumentPerson? InitiatedBy { get; set; }

    /// <summary>When the money moved or the state changed, which is the document's issue date.</summary>
    public DateTime OccurredAtUtc { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }
}
