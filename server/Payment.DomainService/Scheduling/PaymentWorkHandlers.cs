using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;

namespace Payment.DomainService.Scheduling;

/// <summary>
/// The handlers, each delegating to the processor that already owns its rules.
/// </summary>
/// <remarks>
/// Deliberately thin, and that is what makes a retried item safe: the second attempt walks the same
/// code that recognizes the first attempt's provider call rather than raising a new one. A handler
/// that reimplemented a recovery rule would be a second opinion about money that has already moved.
/// </remarks>
public sealed class PaymentRecoveryWorkHandler : IPaymentWorkHandler
{
    private readonly IPaymentRecoveryProcessor _recovery;

    public PaymentRecoveryWorkHandler(IPaymentRecoveryProcessor recovery) => _recovery = recovery;

    public PaymentWorkType WorkType => PaymentWorkType.PaymentRecovery;

    public async Task<PaymentWorkOutcome> ExecuteAsync(
        PaymentBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _recovery.RecoverStaleAsync(work.TenantId, cancellationToken);

        return PaymentWorkOutcome.Completed();
    }
}

public sealed class CaptureRecoveryWorkHandler : IPaymentWorkHandler
{
    private readonly IPaymentCaptureRecoveryProcessor _captures;

    public CaptureRecoveryWorkHandler(IPaymentCaptureRecoveryProcessor captures) =>
        _captures = captures;

    public PaymentWorkType WorkType => PaymentWorkType.CaptureRecovery;

    public async Task<PaymentWorkOutcome> ExecuteAsync(
        PaymentBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _captures.RecoverDueAsync(work.TenantId, cancellationToken);

        return PaymentWorkOutcome.Completed();
    }
}

public sealed class RefundRecoveryWorkHandler : IPaymentWorkHandler
{
    private readonly IPaymentRefundRecoveryProcessor _refunds;

    public RefundRecoveryWorkHandler(IPaymentRefundRecoveryProcessor refunds) => _refunds = refunds;

    public PaymentWorkType WorkType => PaymentWorkType.RefundRecovery;

    public async Task<PaymentWorkOutcome> ExecuteAsync(
        PaymentBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _refunds.RecoverDueAsync(work.TenantId, cancellationToken);

        return PaymentWorkOutcome.Completed();
    }
}

public sealed class PaymentOutboxWorkHandler : IPaymentWorkHandler
{
    private readonly IPaymentOutboxProcessor _outbox;

    public PaymentOutboxWorkHandler(IPaymentOutboxProcessor outbox) => _outbox = outbox;

    public PaymentWorkType WorkType => PaymentWorkType.OutboxPublication;

    public async Task<PaymentWorkOutcome> ExecuteAsync(
        PaymentBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _outbox.PublishDueAsync(work.TenantId, cancellationToken);

        return PaymentWorkOutcome.Completed();
    }
}

public sealed class RefundOutboxWorkHandler : IPaymentWorkHandler
{
    private readonly IPaymentRefundOutboxProcessor _refundOutbox;

    public RefundOutboxWorkHandler(IPaymentRefundOutboxProcessor refundOutbox) =>
        _refundOutbox = refundOutbox;

    public PaymentWorkType WorkType => PaymentWorkType.RefundOutboxPublication;

    public async Task<PaymentWorkOutcome> ExecuteAsync(
        PaymentBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _refundOutbox.PublishDueAsync(work.TenantId, cancellationToken);

        return PaymentWorkOutcome.Completed();
    }
}
