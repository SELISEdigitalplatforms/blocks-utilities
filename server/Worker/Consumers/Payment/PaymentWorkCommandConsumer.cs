using System.Diagnostics;
using Blocks.Genesis;
using Microsoft.Extensions.DependencyInjection;
using Payment.DomainService.Commands;
using Payment.DomainService.Outbox;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Outbox;

namespace Worker.Consumers.Payment;

public sealed class PaymentWorkCommandConsumer :
    IConsumer<ProcessPaymentWorkCommand>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentWorkCommandConsumer> _logger;

    public PaymentWorkCommandConsumer(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentWorkCommandConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Consume(ProcessPaymentWorkCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.TenantId))
        {
            throw new InvalidOperationException(
                "A payment work command requires a tenant identifier.");
        }

        using var serviceScope = _scopeFactory.CreateScope();
        var services = serviceScope.ServiceProvider;
        var contexts = services.GetRequiredService<
            IPaymentTenantContextScopeFactory>();
        using var context = contexts.Establish(command.TenantId);

        // The dispatcher's correlation id, so the request that scheduled this work and this run
        // share one identifier. Commands enqueued before the field existed carry none, and are
        // given a synthetic id that says so rather than an anonymous GUID.
        var runId = Guid.NewGuid().ToString("N");
        var correlationId = string.IsNullOrWhiteSpace(command.CorrelationId)
            ? $"uncorrelated-{runId}"
            : command.CorrelationId;

        using var correlation = PaymentCorrelation.Begin(correlationId);
        using var logScope = PaymentLogScope.Begin(
            _logger,
            PaymentOperations.WorkConsume,
            command.TenantId,
            extra: new Dictionary<string, object?>
            {
                ["IncludeRecovery"] = command.IncludeRecovery,
                ["RunId"] = runId
            });

        var stopwatch = Stopwatch.StartNew();

        // How long the command sat in the queue. Work that is merely late and work that is
        // never picked up are indistinguishable without it.
        _logger.LogInformation(
            "Payment work command processing started Phase={Phase} QueueLatencyMs={QueueLatencyMs}",
            PaymentPhases.Started,
            command.DispatchedAtUtc.HasValue
                ? (DateTime.UtcNow - command.DispatchedAtUtc.Value).TotalMilliseconds
                : -1);

        try
        {
            await RunAsync(command, services, stopwatch);
        }
        catch (Exception exception)
        {
            // Logged and rethrown: rethrowing is what lets the queue retry, and without the log
            // line the only record of the failure lives wherever the queue puts it.
            _logger.LogError(
                exception,
                "Payment work command processing failed Phase={Phase} DurationMs={DurationMs}",
                PaymentPhases.Failed,
                stopwatch.Elapsed.TotalMilliseconds);

            throw;
        }
    }

    private async Task RunAsync(
        ProcessPaymentWorkCommand command,
        IServiceProvider services,
        Stopwatch stopwatch)
    {
        var webhooks = services.GetRequiredService<
            IPaymentWebhookProcessor>();
        var paymentOutbox = services.GetRequiredService<
            IPaymentOutboxProcessor>();
        var refundOutbox = services.GetRequiredService<
            IPaymentRefundOutboxProcessor>();
        var webhookResult =
            await webhooks.ProcessDueAsync(
                command.TenantId,
                CancellationToken.None);
        var processedWebhooks = webhookResult.ProcessedCount;
        var publishedPaymentEvents =
            await paymentOutbox.PublishDueAsync(
                command.TenantId,
                CancellationToken.None);
        var publishedRefundEvents =
            await refundOutbox.PublishDueAsync(
                command.TenantId,
                CancellationToken.None);

        if (command.IncludeRecovery)
        {
            var paymentRecovery = services.GetRequiredService<
                IPaymentRecoveryProcessor>();
            var storedMethodRecovery = services.GetRequiredService<
                IStoredPaymentMethodRemovalRecoveryProcessor>();
            var refundRecovery = services.GetRequiredService<
                IPaymentRefundRecoveryProcessor>();
            var captureRecovery = services.GetRequiredService<
                IPaymentCaptureRecoveryProcessor>();

            await paymentRecovery.RecoverStaleAsync(
                command.TenantId,
                CancellationToken.None);
            await storedMethodRecovery.RecoverDueRemovalsAsync(
                command.TenantId,
                CancellationToken.None);
            await refundRecovery.RecoverDueAsync(
                command.TenantId,
                CancellationToken.None);
            await captureRecovery.RecoverDueAsync(
                command.TenantId,
                CancellationToken.None);

            publishedPaymentEvents +=
                await paymentOutbox.PublishDueAsync(
                    command.TenantId,
                    CancellationToken.None);
            publishedRefundEvents +=
                await refundOutbox.PublishDueAsync(
                    command.TenantId,
                    CancellationToken.None);
        }

        // Subscriptions ride the payment tick rather than their own queue. Every inbound
        // webhook already dispatches this command, so a paid subscription activates within
        // milliseconds of the confirmation that paid for it — with no new bus plumbing and no
        // change to how payments behave.
        var subscriptionActivation = services.GetRequiredService<
            ISubscriptionActivationProcessor>();
        var subscriptionOutbox = services.GetRequiredService<
            ISubscriptionOutboxProcessor>();
        // The fast path. This tick holds both the confirmation and the subscription waiting on
        // it, so settle that exact link now rather than waiting for its deferred next-check to
        // come round — or, failing that, the two-minute repair sweep. Runs before the sweep
        // below so a link settled here is no longer pending and is not looked at twice.
        var targetedSettlements =
            await subscriptionActivation.SettleForPaymentsAsync(
                command.TenantId,
                webhookResult.TransitionedPaymentDetailIds,
                CancellationToken.None);
        var activatedSubscriptions =
            await subscriptionActivation.ProcessDueAsync(
                command.TenantId,
                CancellationToken.None);
        var publishedSubscriptionEvents =
            await subscriptionOutbox.PublishDueAsync(
                command.TenantId,
                CancellationToken.None);

        _logger.LogInformation(
            "Payment work command processing completed Phase={Phase} DurationMs={DurationMs} ProcessedWebhookCount={ProcessedWebhookCount} PublishedPaymentEventCount={PublishedPaymentEventCount} PublishedRefundEventCount={PublishedRefundEventCount} TargetedSettlementCount={TargetedSettlementCount} ActivatedSubscriptionCount={ActivatedSubscriptionCount} PublishedSubscriptionEventCount={PublishedSubscriptionEventCount}",
            PaymentPhases.Completed,
            stopwatch.Elapsed.TotalMilliseconds,
            processedWebhooks,
            publishedPaymentEvents,
            publishedRefundEvents,
            targetedSettlements,
            activatedSubscriptions,
            publishedSubscriptionEvents);
    }
}
