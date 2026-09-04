using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// What a webhook pass did: how many records it processed, and which payments those records
/// moved.
/// </summary>
/// <remarks>
/// The ids are reported because a caller that can see both domains — the worker — can settle the
/// subscription waiting on a payment in the same tick the confirmation arrived, instead of
/// leaving it to the next repair sweep. Nothing in the payment domain reads them.
/// </remarks>
public sealed record PaymentWebhookProcessingResult(
    int ProcessedCount,
    IReadOnlyList<string> TransitionedPaymentDetailIds)
{
    public static PaymentWebhookProcessingResult Empty { get; } =
        new(0, Array.Empty<string>());
}

public interface IPaymentWebhookProcessor
{
    Task<PaymentWebhookProcessingResult> ProcessDueAsync(string tenantId, CancellationToken cancellationToken);
}
