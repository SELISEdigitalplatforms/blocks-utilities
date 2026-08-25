using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// An invoice, a trial invoice or a credit note, as this application issued it.
/// </summary>
/// <remarks>
/// Append-only, and the reason the whole feature exists. The payment provider's own invoices were
/// the only durable statement of what a subscriber was charged, which put the record of our revenue
/// inside somebody else's product: unreachable when they are down, gone when we change processor, and
/// shaped by their template rather than ours.
/// <para>
/// Every party, price and amount is <em>copied</em> here at issue. Nothing on a document is a
/// reference to something that can still move — not the plan, not the price, not the subscriber's own
/// address. A document answers "what was true when this was issued", and the catalogue answers "what
/// is true now"; a join between them would quietly replace the first question with the second.
/// </para>
/// <para>
/// Corrections are made by issuing a credit note and, where needed, a replacement invoice. No issued
/// financial field is ever updated — not to fix a typo, not to fix an error. <see cref="Status"/> and
/// <see cref="Delivery"/> move; the money does not.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionFinancialDocument
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The human-facing number — <c>INV-2026-000123</c>. Unique per tenant, allocated once.
    /// </summary>
    public string DocumentNumber { get; set; } = string.Empty;

    public FinancialDocumentType DocumentType { get; set; }

    public FinancialDocumentStatus Status { get; set; } = FinancialDocumentStatus.Issued;

    /// <summary>
    /// When the document was issued, which is when the money moved rather than when this row was
    /// written.
    /// </summary>
    /// <remarks>
    /// Taken from the payment, refund or trial start it describes. The two can differ by however long
    /// the queue was behind, and a document dated by its own bookkeeping would put a December charge
    /// in January's numbers.
    /// </remarks>
    public DateTime IssuedAtUtc { get; set; }

    public string TenantId { get; set; } = string.Empty;

    /// <summary>The subscribing organization — the customer, never the merchant.</summary>
    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>
    /// What in the money path this document was issued for. Exactly one is set, and which one it is
    /// says what kind of event produced the document.
    /// </summary>
    public string? PaymentDetailId { get; set; }

    public string? RefundId { get; set; }

    /// <summary>
    /// Which change this document settles: the settlement reservation where there was one, otherwise
    /// the subscription version the change was applied against.
    /// </summary>
    /// <remarks>
    /// Two shapes in one field because a downgrade has no reservation to name. It charges nothing, so
    /// nothing is reserved before it commits, and the versioned write it does take is the thing that
    /// can only succeed once — the same guarantee by another route. Named for the reservation because
    /// that is what it is on every document that has one.
    /// </remarks>
    public string? SettlementReservationId { get; set; }

    /// <summary>The invoice a credit note adjusts. Null on invoices.</summary>
    public string? OriginalDocumentId { get; set; }

    public string? OriginalDocumentNumber { get; set; }

    /// <summary>
    /// The one value that makes issuing idempotent, under a unique index.
    /// </summary>
    /// <remarks>
    /// Derived from the source event and nothing else — a payment id, a refund id, a reservation id, a
    /// subscription and a trial instant. A replayed activation, a re-delivered webhook, a recovery
    /// sweep and a second worker all compute the same key, so the second insert loses the race
    /// instead of allocating a second number and sending a second email.
    /// <para>
    /// This is what makes the document ledger exactly-once without a distributed lock. See
    /// <c>FinancialDocumentSourceKey</c> for the derivations.
    /// </para>
    /// </remarks>
    public string SourceKey { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>Who issued it — us, as configured for this tenant.</summary>
    public FinancialDocumentMerchant Merchant { get; set; } = new();

    /// <summary>Who it is addressed to, copied from the billing profile at issue.</summary>
    public FinancialDocumentParty Subscriber { get; set; } = new();

    public FinancialDocumentPerson BillingContact { get; set; } = new();

    /// <summary>
    /// Who asked for the thing being billed. A worker-created renewal names no person.
    /// </summary>
    public FinancialDocumentPerson InitiatedBy { get; set; } = new();

    public FinancialDocumentSubject Subject { get; set; } = new();

    /// <summary>The trial's own terms, on a trial invoice. Null on every other kind.</summary>
    public FinancialDocumentTrial? Trial { get; set; }

    /// <summary>What the document covers, in the subscriber's own timezone and in UTC.</summary>
    public FinancialDocumentPeriod Period { get; set; } = new();

    public FinancialDocumentAmounts Amounts { get; set; } = new();

    /// <summary>
    /// The two sides of a plan or quantity change, when that is what this settles.
    /// </summary>
    /// <remarks>
    /// Reused from the payment module rather than restated here. It is the same subtraction, recorded
    /// at quote time on the reservation and carried onto the payment — copying its shape would give
    /// one calculation two definitions.
    /// </remarks>
    public SubscriptionSettlementBreakdown? Settlement { get; set; }

    public List<FinancialDocumentLine> Lines { get; set; } = [];

    public FinancialDocumentDelivery Delivery { get; set; } = new();

    public string CorrelationId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The merchant's identity as it must appear on the document.
/// </summary>
/// <remarks>
/// Snapshotted like everything else. A tenant that rebrands, moves office or changes bank must not
/// rewrite the letterhead on invoices already sent — and a subscriber querying a two-year-old charge
/// needs the payment instructions that were on it, not today's.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class FinancialDocumentMerchant
{
    public string LegalName { get; set; } = string.Empty;

    /// <summary>The trading name, where it differs from the registered one.</summary>
    public string? DisplayName { get; set; }

    public BillingAddress? Address { get; set; }

    public string? TaxRegistrationId { get; set; }

    public string? SupportEmail { get; set; }

    /// <summary>Free text — bank details, terms, a VAT note. Rendered verbatim in the footer.</summary>
    public string? PaymentInstructions { get; set; }
}

/// <summary>The subscriber, as of the issue date.</summary>
[BsonIgnoreExtraElements]
public sealed class FinancialDocumentParty
{
    public string OrganizationId { get; set; } = string.Empty;

    public string LegalName { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public BillingAddress? Address { get; set; }

    public string? TaxRegistrationId { get; set; }
}

/// <summary>
/// A named person on the document: the billing contact, or whoever initiated the charge.
/// </summary>
/// <remarks>
/// <see cref="UserId"/> is null where no person acted. A renewal is initiated by the clock, and
/// inventing a user for it would attribute a charge to whoever last touched the subscription.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class FinancialDocumentPerson
{
    public string? UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }
}

/// <summary>What was subscribed to, as sold.</summary>
[BsonIgnoreExtraElements]
public sealed class FinancialDocumentSubject
{
    public string PlanCode { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public string PriceId { get; set; } = string.Empty;

    public BillingInterval Interval { get; set; } = BillingInterval.Month;

    public int IntervalCount { get; set; } = 1;

    public long UnitAmountMinor { get; set; }
}

/// <summary>The trial a trial invoice states the terms of.</summary>
[BsonIgnoreExtraElements]
public sealed class FinancialDocumentTrial
{
    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    /// <summary>Whether a card was taken up front — the difference the subscriber cares about.</summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>When the first real charge is expected. Null if nothing is scheduled.</summary>
    public DateTime? FirstBillingAtUtc { get; set; }
}

/// <summary>
/// The service period, stated twice.
/// </summary>
/// <remarks>
/// In UTC because that is what every other record here uses and the only way two documents can be
/// compared, and in the subscriber's own timezone because that is the boundary they experience — a
/// period that ends at 23:00 UTC ends on the following day in Auckland, and an invoice that says
/// otherwise is wrong to the person reading it.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class FinancialDocumentPeriod
{
    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>Local calendar dates, formatted at issue so rendering needs no timezone database.</summary>
    public string LocalStart { get; set; } = string.Empty;

    public string LocalEnd { get; set; } = string.Empty;

    /// <summary>
    /// The billing period this belongs to — the key renewals are scoped by. Empty on documents that
    /// do not belong to one, such as a settlement.
    /// </summary>
    public string PeriodKey { get; set; } = string.Empty;

    /// <summary>Whether the period is a fraction of a full interval.</summary>
    public bool IsProrated { get; set; }

    public int? ProratedDays { get; set; }

    public int? ProratedTotalDays { get; set; }
}

/// <summary>
/// Every figure on the document, in minor units.
/// </summary>
/// <remarks>
/// Each discount source recorded separately, not as one total. "Something came off" cannot be turned
/// back into "the annual price gave 8% and the coupon gave nothing", and which of the two it was is
/// exactly what somebody reconciling a two-year-old invoice needs — by which time the catalogue has
/// moved on and the coupon has been retired.
/// <para>
/// <see cref="TotalMinor"/> is stored rather than derived, because it is the figure that has to
/// reconcile to the minor unit against what the provider actually took. A total recomputed at render
/// time from fields that were themselves rounded is a total that can disagree with the bank.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class FinancialDocumentAmounts
{
    /// <summary>The charge before anything came off.</summary>
    public long GrossSubtotalMinor { get; set; }

    /// <summary>The price's own automatic rate, as money.</summary>
    public long AutomaticDiscountMinor { get; set; }

    /// <summary>The volume band the purchased quantity selected, as money.</summary>
    public long QuantityDiscountMinor { get; set; }

    /// <summary>A redeemed code, applied after the two above were settled between themselves.</summary>
    public long PromotionalDiscountMinor { get; set; }

    /// <summary>Gross less all three discounts. What tax is calculated on.</summary>
    public long NetSubtotalMinor { get; set; }

    public int? TaxRateBasisPoints { get; set; }

    /// <summary>Whether the rate was added to the net or already inside it.</summary>
    public string? TaxMode { get; set; }

    public long TaxAmountMinor { get; set; }

    /// <summary>
    /// Banked credit spent against this invoice. A deduction, not a discount: it pays the bill rather
    /// than changing what the bill was for, so it sits below tax.
    /// </summary>
    public long CreditAppliedMinor { get; set; }

    /// <summary>What was actually charged, or on a credit note what was returned.</summary>
    public long TotalMinor { get; set; }

    public int? AutomaticDiscountBasisPoints { get; set; }

    public int? QuantityDiscountBasisPoints { get; set; }

    /// <summary>How the two built-in discounts met — the price's authored combination.</summary>
    public string? DiscountCombination { get; set; }

    /// <summary>The code that was redeemed, when one was. Never the discount's internal id.</summary>
    public string? PromotionCode { get; set; }
}

/// <summary>One priced line on the document.</summary>
[BsonIgnoreExtraElements]
public sealed class FinancialDocumentLine
{
    public string Description { get; set; } = string.Empty;

    /// <summary>Seats, units, metered events. Null for a line that is not counted.</summary>
    public long? Quantity { get; set; }

    public long? UnitAmountMinor { get; set; }

    public long AmountMinor { get; set; }

    /// <summary>A quantity item key or a meter key, so a client can group lines it recognises.</summary>
    public string? ItemKey { get; set; }
}

/// <summary>
/// The PDF and the email: where they got to, and what has been tried.
/// </summary>
/// <remarks>
/// The only mutable part of a document, and mutable only forwards. <see cref="StorageId"/> and
/// <see cref="ContentHash"/> are written once and never rewritten: an issued PDF is not regenerated
/// against a newer template, because the file the subscriber already has is the document.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class FinancialDocumentDelivery
{
    public FinancialDocumentDeliveryState State { get; set; } =
        FinancialDocumentDeliveryState.Pending;

    /// <summary>The file-storage id of the rendered PDF. Null until it has been rendered once.</summary>
    public string? StorageId { get; set; }

    /// <summary>
    /// SHA-256 of the stored bytes, lowercase hex. What proves the file was not replaced.
    /// </summary>
    public string? ContentHash { get; set; }

    public long? ContentLength { get; set; }

    public DateTime? GeneratedAtUtc { get; set; }

    /// <summary>
    /// The identity of the mail this document sends, derived from the document id.
    /// </summary>
    /// <remarks>
    /// Published inside the message so the mail consumer can recognise a repeat. Publishing to a bus
    /// and recording that it happened are two writes with no transaction between them, so a crash in
    /// that window leaves a message that may or may not have gone out — and the only honest answer is
    /// to republish under the same identity and let the consumer decide. Derived rather than generated
    /// for exactly that reason: a fresh id on the retry would make the duplicate undetectable.
    /// </remarks>
    public string? MailMessageId { get; set; }

    /// <summary>
    /// When the mail was first handed to the bus, recorded <em>before</em> handing it over.
    /// </summary>
    /// <remarks>
    /// Recorded first so that a retry can tell "never published" from "may have published", which are
    /// different situations: the first must publish, the second must publish again knowing a duplicate
    /// is possible, and neither should be guessed at.
    /// </remarks>
    public DateTime? MailRequestedAtUtc { get; set; }

    public DateTime? EmailedAtUtc { get; set; }

    public int AttemptCount { get; set; }

    /// <summary>A classification, never a provider or template message.</summary>
    public string? LastErrorCode { get; set; }

    public DateTime? LastAttemptAtUtc { get; set; }
}
