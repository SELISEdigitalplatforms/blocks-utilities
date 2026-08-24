using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Services;

namespace Payment.DomainService.Scheduling;

public sealed class PaymentReconciliationWorkHandler : IPaymentWorkHandler
{
    private readonly IPaymentRecoveryProcessor _payments;
    private readonly IPaymentCaptureRecoveryProcessor _captures;
    private readonly IPaymentRefundRecoveryProcessor _refunds;
    private readonly IPaymentOutboxProcessor _paymentOutbox;
    private readonly IPaymentRefundOutboxProcessor _refundOutbox;

    public PaymentReconciliationWorkHandler(
        IPaymentRecoveryProcessor payments,
        IPaymentCaptureRecoveryProcessor captures,
        IPaymentRefundRecoveryProcessor refunds,
        IPaymentOutboxProcessor paymentOutbox,
        IPaymentRefundOutboxProcessor refundOutbox)
    {
        _payments = payments;
        _captures = captures;
        _refunds = refunds;
        _paymentOutbox = paymentOutbox;
        _refundOutbox = refundOutbox;
    }

    public PaymentWorkType WorkType => PaymentWorkType.PaymentReconciliation;

    public async Task<PaymentWorkOutcome> ExecuteAsync(PaymentBackgroundWork work, CancellationToken token)
    {
        await _payments.RecoverStaleAsync(work.TenantId, token);
        await _captures.RecoverDueAsync(work.TenantId, token);
        await _refunds.RecoverDueAsync(work.TenantId, token);
        await _paymentOutbox.PublishDueAsync(work.TenantId, token);
        await _refundOutbox.PublishDueAsync(work.TenantId, token);
        return PaymentWorkOutcome.Completed();
    }
}

public sealed class WebhookRecoveryWorkHandler : IPaymentWorkHandler
{
    private readonly IPaymentWebhookProcessor _webhooks;
    public WebhookRecoveryWorkHandler(IPaymentWebhookProcessor webhooks) => _webhooks = webhooks;
    public PaymentWorkType WorkType => PaymentWorkType.WebhookRecovery;

    public async Task<PaymentWorkOutcome> ExecuteAsync(PaymentBackgroundWork work, CancellationToken token)
    {
        await _webhooks.ProcessDueAsync(work.TenantId, token);
        return PaymentWorkOutcome.Completed();
    }
}

public sealed class ProviderStateRefreshWorkHandler : IPaymentWorkHandler
{
    private readonly IPaymentRecoveryProcessor _payments;
    public ProviderStateRefreshWorkHandler(IPaymentRecoveryProcessor payments) => _payments = payments;
    public PaymentWorkType WorkType => PaymentWorkType.ProviderStateRefresh;

    public async Task<PaymentWorkOutcome> ExecuteAsync(PaymentBackgroundWork work, CancellationToken token)
    {
        await _payments.RecoverStaleAsync(work.TenantId, token);
        return PaymentWorkOutcome.Completed();
    }
}

public sealed class StoredPaymentCleanupWorkHandler : IPaymentWorkHandler
{
    private readonly IStoredPaymentMethodRemovalRecoveryProcessor _removals;
    public StoredPaymentCleanupWorkHandler(IStoredPaymentMethodRemovalRecoveryProcessor removals) =>
        _removals = removals;
    public PaymentWorkType WorkType => PaymentWorkType.StoredPaymentCleanup;

    public async Task<PaymentWorkOutcome> ExecuteAsync(PaymentBackgroundWork work, CancellationToken token)
    {
        await _removals.RecoverDueRemovalsAsync(work.TenantId, token);
        return PaymentWorkOutcome.Completed();
    }
}
