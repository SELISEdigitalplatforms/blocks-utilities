using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentRefundWebhookStateTransitionService :
    IPaymentRefundWebhookStateTransitionService
{
    private readonly IPaymentRefundRepository _refunds;
    private readonly ICurrencyMinorUnitResolver _minorUnits;
    private readonly IPaymentRefundOutboxEventFactory _events;
    private readonly ILogger<
        PaymentRefundWebhookStateTransitionService> _logger;

    public PaymentRefundWebhookStateTransitionService(
        IPaymentRefundRepository refunds,
        ICurrencyMinorUnitResolver minorUnits,
        IPaymentRefundOutboxEventFactory events,
        ILogger<
            PaymentRefundWebhookStateTransitionService> logger)
    {
        _refunds = refunds;
        _minorUnits = minorUnits;
        _events = events;
        _logger = logger;
    }

    public async Task ApplyAsync(
        PaymentWebhookInbox webhook,
        CancellationToken cancellationToken)
    {
        var payload = webhook.NormalizedPayload;

        if (string.IsNullOrWhiteSpace(payload.RefundId) ||
            string.IsNullOrWhiteSpace(
                payload.PaymentDetailId) ||
            string.IsNullOrWhiteSpace(
                payload.PspReference) ||
            string.IsNullOrWhiteSpace(
                payload.OriginalPspReference) ||
            !payload.Success.HasValue)
        {
            throw new InvalidOperationException(
                "Incomplete normalized refund event.");
        }

        var payment =
            await _refunds.GetPaymentByRefundIdAsync(
                webhook.TenantId,
                payload.RefundId,
                cancellationToken);
        var refund = payment?.Refunds.FirstOrDefault(
            candidate =>
                candidate.RefundId == payload.RefundId);

        if (payment == null ||
            refund == null ||
            payment.ItemId != payload.PaymentDetailId)
        {
            throw new InvalidOperationException(
                "The refund reference was not found.");
        }

        if (!string.Equals(
                refund.OriginalPaymentPspReference,
                payload.OriginalPspReference,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The original payment reference did not match.");
        }

        if (!_minorUnits.TryConvert(
                refund.Amount,
                refund.CurrencyCode,
                out var expectedMinorUnits) ||
            payload.AmountMinorUnits !=
            expectedMinorUnits ||
            !string.Equals(
                payload.CurrencyCode,
                refund.CurrencyCode,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The refund amount did not match.");
        }

        var transition = ResolveTransition(
            webhook.EventCode,
            payload.Success.Value,
            payment,
            refund,
            payload);

        if (transition == null)
        {
            _logger.LogInformation(
                "Payment refund webhook skipped EventCode={EventCode} RefundStatus={RefundStatus}",
                PaymentLogValue.Label(webhook.EventCode),
                PaymentLogValue.Label(refund.Status));

            return;
        }

        var outbox = _events.Create(
            payment,
            refund,
            transition.EventType,
            transition.TargetStatus);
        outbox.DeduplicationKey =
            $"{refund.RefundId}:{transition.EventType}:{payload.PspReference}";

        var applied =
            await _refunds.ApplyProviderEventAsync(
                webhook.TenantId,
                payment.ItemId,
                refund.RefundId,
                transition.ExpectedStatuses,
                transition.TargetStatus,
                payload.PspReference,
                webhook.EventDateUtc,
                transition.ReservedAmountDelta,
                transition.RefundedAmountDelta,
                transition.TargetPaymentStatus,
                transition.CompletionAction,
                transition.FailureCode,
                transition.FailureSummary,
                outbox,
                cancellationToken);

        _logger.LogInformation(
            "Payment refund webhook transition completed EventCode={EventCode} TargetStatus={TargetStatus} Applied={Applied} PaymentHash={PaymentHash} RefundHash={RefundHash}",
            PaymentLogValue.Label(webhook.EventCode),
            transition.TargetStatus,
            applied,
            PaymentLogValue.Hash(payment.ItemId),
            PaymentLogValue.Hash(refund.RefundId));
    }

    private static RefundTransition? ResolveTransition(
        string eventCode,
        bool success,
        PaymentDetail payment,
        PaymentRefund refund,
        PaymentWebhookPayload payload)
    {
        if (eventCode.Equals(
                "REFUND",
                StringComparison.OrdinalIgnoreCase))
        {
            return success
                ? new RefundTransition(
                    [
                        PaymentRefundStatuses.Initiating,
                        PaymentRefundStatuses
                            .InitiationUnknown,
                        PaymentRefundStatuses.Submitted
                    ],
                    PaymentRefundStatuses.Succeeded,
                    PaymentConstants
                        .PaymentRefundSucceeded,
                    -refund.Amount,
                    refund.Amount,
                    payment.RefundedAmount + refund.Amount >=
                    payment.CapturedAmount
                        ? PaymentStatuses.Refunded
                        : PaymentStatuses.PartiallyRefunded,
                    PaymentFundReturnOperations.Refund,
                    null,
                    null)
                : FailureFromCurrent(
                    payment,
                    refund,
                    payload.ProviderFailureCode,
                    payload.ProviderFailureSummary);
        }

        if (eventCode.Equals(
                "CANCEL_OR_REFUND",
                StringComparison.OrdinalIgnoreCase) &&
            refund.ProviderOperation ==
            PaymentFundReturnOperations.Reversal)
        {
            if (!success)
            {
                return FailureFromCurrent(
                    payment,
                    refund,
                    payload.ProviderFailureCode,
                    payload.ProviderFailureSummary);
            }

            var completionAction = ResolveReversalAction(
                payload.ModificationAction,
                payment);
            var cancelled = completionAction == "cancel";

            return new RefundTransition(
                [
                    PaymentRefundStatuses.Initiating,
                    PaymentRefundStatuses.InitiationUnknown,
                    PaymentRefundStatuses.Submitted
                ],
                PaymentRefundStatuses.Succeeded,
                cancelled
                    ? PaymentConstants.PaymentCancelled
                    : PaymentConstants.PaymentRefundSucceeded,
                -refund.Amount,
                cancelled ? 0 : refund.Amount,
                cancelled
                    ? PaymentStatuses.Cancelled
                    : PaymentStatuses.Refunded,
                completionAction,
                null,
                null);
        }

        if (eventCode.Equals(
                "REFUND_FAILED",
                StringComparison.OrdinalIgnoreCase))
        {
            return FailureFromCurrent(
                payment,
                refund,
                payload.ProviderFailureCode,
                payload.ProviderFailureSummary);
        }

        if (eventCode.Equals(
                "REFUNDED_REVERSED",
                StringComparison.OrdinalIgnoreCase) &&
            refund.Status ==
            PaymentRefundStatuses.Succeeded)
        {
            return new RefundTransition(
                [PaymentRefundStatuses.Succeeded],
                PaymentRefundStatuses.Reversed,
                PaymentConstants.PaymentRefundReversed,
                0,
                -refund.Amount,
                PaymentStatuses.Captured,
                "reversed",
                payload.ProviderFailureCode,
                payload.ProviderFailureSummary);
        }

        return null;
    }

    private static RefundTransition? FailureFromCurrent(
        PaymentDetail payment,
        PaymentRefund refund,
        string? failureCode,
        string? failureSummary) =>
        refund.Status switch
        {
            PaymentRefundStatuses.Initiating or
            PaymentRefundStatuses.InitiationUnknown or
            PaymentRefundStatuses.Submitted =>
                new RefundTransition(
                    [
                        PaymentRefundStatuses.Initiating,
                        PaymentRefundStatuses
                            .InitiationUnknown,
                        PaymentRefundStatuses.Submitted
                    ],
                    PaymentRefundStatuses.Failed,
                    PaymentConstants.PaymentRefundFailed,
                    -refund.Amount,
                    0,
                    payment.PaymentStatus,
                    null,
                    failureCode ?? "payment_refund_provider_failed",
                    failureSummary ??
                    "The provider rejected the fund return."),
            PaymentRefundStatuses.Succeeded =>
                new RefundTransition(
                    [PaymentRefundStatuses.Succeeded],
                    PaymentRefundStatuses.Failed,
                    PaymentConstants.PaymentRefundFailed,
                    0,
                    -refund.Amount,
                    payment.PaymentStatus,
                    null,
                    failureCode ?? "payment_refund_provider_failed",
                    failureSummary ??
                    "The provider rejected the fund return."),
            _ => null
        };

    private static string ResolveReversalAction(
        string? providerAction,
        PaymentDetail payment)
    {
        if (providerAction?.Equals(
                "cancel",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return "cancel";
        }

        if (providerAction?.Equals(
                "refund",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return "refund";
        }

        return payment.CapturedAmount > 0
            ? "refund"
            : "cancel";
    }

    private sealed record RefundTransition(
        IReadOnlyCollection<string> ExpectedStatuses,
        string TargetStatus,
        string EventType,
        decimal ReservedAmountDelta,
        decimal RefundedAmountDelta,
        string TargetPaymentStatus,
        string? CompletionAction,
        string? FailureCode,
        string? FailureSummary);
}
