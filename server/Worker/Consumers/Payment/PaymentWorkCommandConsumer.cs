using Blocks.Genesis;
using Microsoft.Extensions.DependencyInjection;
using Payment.DomainService.Commands;
using Payment.DomainService.Outbox;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

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
        using var logScope = _logger.BeginScope(
            new Dictionary<string, object?>
            {
                ["TenantHash"] =
                    PaymentLogValue.Hash(command.TenantId),
                ["IncludeRecovery"] = command.IncludeRecovery,
                ["PaymentWorkCommandId"] =
                    Guid.NewGuid().ToString("N")
            });

        _logger.LogInformation(
            "Payment work command processing started");

        var webhooks = services.GetRequiredService<
            IPaymentWebhookProcessor>();
        var paymentOutbox = services.GetRequiredService<
            IPaymentOutboxProcessor>();
        var refundOutbox = services.GetRequiredService<
            IPaymentRefundOutboxProcessor>();
        var processedWebhooks =
            await webhooks.ProcessDueAsync(
                command.TenantId,
                CancellationToken.None);
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

        _logger.LogInformation(
            "Payment work command processing completed ProcessedWebhookCount={ProcessedWebhookCount} PublishedPaymentEventCount={PublishedPaymentEventCount} PublishedRefundEventCount={PublishedRefundEventCount}",
            processedWebhooks,
            publishedPaymentEvents,
            publishedRefundEvents);
    }
}
