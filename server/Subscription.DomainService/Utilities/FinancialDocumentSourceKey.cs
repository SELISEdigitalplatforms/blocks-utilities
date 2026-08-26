using System.Globalization;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// The idempotency key a financial document is issued under, derived from what caused it.
/// </summary>
/// <remarks>
/// One place, because two spellings of the same key is the whole failure mode. A recovery sweep
/// looking for a document that was never issued has to look under exactly the name the settlement
/// path would have written, and if it does not, the sweep issues a second invoice with a second
/// number and sends a second email for money that moved once.
/// <para>
/// Every derivation is from a durable identifier the source event already has — never from a clock,
/// a counter or a random value. That is what lets the key be recomputed after the process that first
/// computed it is gone, which is the only situation in which recovery is needed at all.
/// </para>
/// </remarks>
public static class FinancialDocumentSourceKey
{
    /// <summary>
    /// A settled charge, keyed on the payment that settled it.
    /// </summary>
    /// <remarks>
    /// The payment detail id rather than the order id, and not the provider's reference. One
    /// subscription's order id is shared across every retry of a period, so keying on it would fold
    /// a genuinely second charge into the first document; the provider's reference is absent until
    /// the provider answers, which is after the point at which this has to be decidable.
    /// </remarks>
    public static string ForPayment(string paymentDetailId) =>
        $"payment:{Require(paymentDetailId, nameof(paymentDetailId))}";

    /// <summary>
    /// A trial that began, keyed on the subscription and the instant it began.
    /// </summary>
    /// <remarks>
    /// The instant is part of it because one subscription can trial more than once over its life —
    /// a re-subscribe after cancellation, a trial granted again by support. Keyed on the
    /// subscription alone, the second trial would silently reuse the first document.
    /// <para>
    /// Formatted round-trip so the same instant always spells the same key regardless of the
    /// machine's culture, which a default <c>ToString</c> does not guarantee. The kind is <em>stamped</em>
    /// rather than converted, because every instant in this module is already UTC and converting an
    /// <c>Unspecified</c> one — which is what some deserializers hand back — would shift it by the
    /// server's own offset and produce a second key for one trial.
    /// </para>
    /// </remarks>
    public static string ForTrial(string subscriptionId, DateTime trialStartUtc) =>
        $"trial:{Require(subscriptionId, nameof(subscriptionId))}:" +
        DateTime.SpecifyKind(trialStartUtc, DateTimeKind.Utc)
            .ToString("O", CultureInfo.InvariantCulture);

    /// <summary>A confirmed refund, keyed on the refund itself.</summary>
    public static string ForRefund(string refundId) =>
        $"refund:{Require(refundId, nameof(refundId))}";

    /// <summary>
    /// A downgrade that banked credit, keyed on the change that banked it.
    /// </summary>
    /// <param name="reference">
    /// What identifies this one downgrade. The settlement reservation where there is one; otherwise
    /// the subscription version the change was applied against, which is the same guarantee by
    /// another route — a downgrade that banks credit charges nothing, so it takes no reservation, and
    /// the versioned write it does take can succeed exactly once.
    /// </param>
    public static string ForDowngradeCredit(string subscriptionId, string reference) =>
        $"downgrade:{Require(subscriptionId, nameof(subscriptionId))}:" +
        Require(reference, nameof(reference));

    private static string Require(string value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException("A non-empty value is required.", parameterName);
}
