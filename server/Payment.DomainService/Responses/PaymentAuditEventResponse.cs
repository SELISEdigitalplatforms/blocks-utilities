using Payment.DomainService.Entities;

namespace Payment.DomainService.Responses;

/// <summary>
/// A payment audit event as an organization may see it: what happened and how it ended, without
/// the actor or user ids, which stay in the database.
/// </summary>
public sealed class PaymentAuditEventResponse
{
    public string EventId { get; init; } = string.Empty;
    public string? OrganizationId { get; init; }
    public string SubjectType { get; init; } = string.Empty;
    public string? SubjectId { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public string? Detail { get; init; }
    public DateTime OccurredAtUtc { get; init; }

    public static PaymentAuditEventResponse From(PaymentAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        return new PaymentAuditEventResponse
        {
            EventId = auditEvent.ItemId,
            OrganizationId = auditEvent.OrganizationId,
            SubjectType = auditEvent.SubjectType,
            SubjectId = auditEvent.SubjectId,
            Operation = auditEvent.Operation,
            Outcome = auditEvent.Outcome,
            ErrorCode = auditEvent.ErrorCode,
            CorrelationId = auditEvent.CorrelationId,
            Amount = auditEvent.Amount,
            CurrencyCode = auditEvent.CurrencyCode,
            Detail = auditEvent.Detail,
            OccurredAtUtc = auditEvent.OccurredAtUtc
        };
    }
}
