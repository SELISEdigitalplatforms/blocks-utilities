using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Services;

/// <summary>What a payment controller knows about an action once it has run.</summary>
public sealed record PaymentAuditEntry(
    string Operation,
    string SubjectType,
    string? SubjectId,
    string Outcome,
    string? ErrorCode = null,
    string? OrganizationId = null,
    decimal? Amount = null,
    string? CurrencyCode = null,
    string? Detail = null,
    PaymentFailureKind FailureKind = PaymentFailureKind.None);

public interface IPaymentAuditTrail
{
    /// <summary>
    /// Records an action under the caller resolved for <paramref name="correlationId"/>. Never
    /// throws: a failed write is logged as <c>PAYMENT_AUDIT_WRITE_FAILED</c> and swallowed, because
    /// a payment that happened must not be reported to its caller as one that did not.
    /// <para>
    /// A <see cref="PaymentFailureKind.RateLimited"/> refusal is not recorded: nothing was attempted,
    /// and recording every throttled call would let one misbehaving client grow a collection that
    /// is kept forever.
    /// </para>
    /// </summary>
    Task RecordAsync(PaymentAuditEntry entry, string correlationId, CancellationToken cancellationToken);

    /// <summary>
    /// The caller's organization's events, or the whole tenant's for the console. Null when the
    /// caller cannot be resolved.
    /// </summary>
    Task<IReadOnlyList<PaymentAuditEvent>?> ListAsync(
        string? subjectType,
        string? subjectId,
        int limit,
        string correlationId,
        CancellationToken cancellationToken);
}
