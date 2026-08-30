using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One record of a mail being handed to the listener, with the payload that was handed over.
/// </summary>
/// <remarks>
/// Written beside the mail rather than inside the thing being mailed. A financial document already
/// carries its own <c>Delivery</c> block, but that block holds the current state of one document's
/// one mail — it is overwritten on a resend, says nothing about usage-threshold mail, and never
/// held what was actually sent. This is the append-only history: one row per handover, keeping the
/// payload as it left.
/// <para>
/// Recording is deliberately best-effort and must never change what happens to the mail. Document
/// delivery is at-most-once and guarded by a claim, so a report write that threw and bubbled up
/// could release that claim and put a second invoice in somebody's inbox — the exact failure the
/// claim exists to prevent. A report is worth strictly less than that guarantee.
/// </para>
/// </remarks>
public sealed class MailDeliveryReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Absent for tenant-wide mail, which is why it is nullable rather than empty.</summary>
    public string? OrganizationId { get; set; }

    public MailDeliveryReportSource Source { get; set; }

    public MailDeliveryReportOutcome Outcome { get; set; }

    /// <summary>
    /// The thing being mailed about: a financial document id, or a subscription id for a usage
    /// warning. Indexed, so "every mail we ever sent about this invoice" is one query.
    /// </summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>
    /// How a human names that subject — a document number, or the lifecycle event id. Carried
    /// alongside the id because an operator reading this collection has the invoice number in
    /// front of them and not a GUID.
    /// </summary>
    public string? SubjectReference { get; set; }

    /// <summary>
    /// The idempotency key the delivery service derives per document, when there is one. Usage
    /// mail has none: it is not claimed and may legitimately be sent more than once.
    /// </summary>
    public string? MailMessageId { get; set; }

    /// <summary>The listener queue the message was addressed to.</summary>
    public string ConsumerName { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    /// <summary>Recipients as they were sent — already trimmed and lowercased by the publisher.</summary>
    public IReadOnlyList<string> To { get; set; } = [];

    public IReadOnlyList<string> Cc { get; set; } = [];

    public IReadOnlyList<string> Bcc { get; set; } = [];

    /// <summary>Storage ids, not file contents. A rendered invoice is megabytes and lives in storage.</summary>
    public IReadOnlyList<string> Attachments { get; set; } = [];

    /// <summary>
    /// The whole <c>SendMail</c> as JSON, exactly as published.
    /// </summary>
    /// <remarks>
    /// The point of the collection. The fields above are duplicated out of this string so the
    /// common questions are indexable and readable without parsing, but the payload is what
    /// answers "what did the template actually receive" when a mail arrives wrong — a
    /// <c>BodyDataContext</c> holding an empty plan name or a stale total cannot be reconstructed
    /// from anything else after the fact.
    /// </remarks>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>SHA-256 of <see cref="PayloadJson"/>, for spotting two handovers that were identical.</summary>
    public string PayloadHash { get; set; } = string.Empty;

    public int PayloadLength { get; set; }

    /// <summary>Set only when <see cref="Outcome"/> is <see cref="MailDeliveryReportOutcome.PublishFailed"/>.</summary>
    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Ties a report to the sweep or request that produced it, and to the work queue item.</summary>
    public string? CorrelationId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When the TTL index removes this row.
    /// </summary>
    /// <remarks>
    /// Always set, unlike the work queue's equivalent, because every row here is already finished
    /// when it is written — there is no pending state that must survive. Payloads carry recipient
    /// addresses and billing figures, so keeping them indefinitely would be accumulating personal
    /// data for no stated purpose.
    /// </remarks>
    public DateTime PurgeAtUtc { get; set; }
}
