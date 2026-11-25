using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Responses;

/// <summary>A safe operator-facing view of a subscription audit event.</summary>
public sealed class SubscriptionAuditEventResponse
{
    public string EventId { get; init; } = string.Empty;
    public string OperationId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public long? AmountMinor { get; init; }
    public string? CurrencyCode { get; init; }
    public string? FromStatus { get; init; }
    public string? ToStatus { get; init; }
    public string? ErrorCode { get; init; }
    public string? FailureKind { get; init; }
    public int? Attempt { get; init; }

    /// <summary>The price a catalogue edit or a scheduled price change concerns.</summary>
    public string? PriceId { get; init; }

    /// <summary>When a scheduled change takes effect, where it is not immediate.</summary>
    public DateTime? EffectiveAtUtc { get; init; }

    /// <summary>What changed, e.g. <c>Amount 8900 → 9900 CHF (minor units)</c>.</summary>
    public string? Detail { get; init; }

    public DateTime OccurredAtUtc { get; init; }

    /// <summary>
    /// The one mapping, so the subscription timeline and a plan's history show events the same way
    /// and neither leaks actor or payment identifiers, nor the operator-typed reason.
    /// </summary>
    public static SubscriptionAuditEventResponse From(SubscriptionAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        return new SubscriptionAuditEventResponse
        {
            EventId = auditEvent.ItemId,
            OperationId = auditEvent.OperationId,
            CorrelationId = auditEvent.CorrelationId,
            Operation = auditEvent.Operation,
            Stage = auditEvent.Stage,
            Outcome = auditEvent.Outcome,
            Source = auditEvent.Source,
            AmountMinor = auditEvent.AmountMinor,
            CurrencyCode = auditEvent.CurrencyCode,
            FromStatus = auditEvent.FromStatus,
            ToStatus = auditEvent.ToStatus,
            ErrorCode = auditEvent.ErrorCode,
            FailureKind = auditEvent.FailureKind,
            Attempt = auditEvent.Attempt,
            PriceId = auditEvent.TargetPriceId,
            EffectiveAtUtc = auditEvent.EffectiveAtUtc,
            Detail = auditEvent.Detail,
            OccurredAtUtc = auditEvent.OccurredAtUtc
        };
    }
}
