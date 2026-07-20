using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Payment.DomainService.Commands;
using Payment.DomainService.Outbox;
using Payment.DomainService.Services;
using Worker.Consumers.Payment;

namespace XUnitTest.Payment;

public sealed class PaymentWorkCommandConsumerTests
{
    private const string TenantId = "tenant-a";

    [Fact]
    public async Task Consume_processes_due_work_without_running_recovery()
    {
        var fixture = new Fixture();

        await fixture.Consumer.Consume(new ProcessPaymentWorkCommand
        {
            TenantId = TenantId,
            IncludeRecovery = false
        });

        fixture.VerifyDueWork(Times.Once());
        fixture.VerifyRecovery(Times.Never());
    }

    [Fact]
    public async Task Consume_runs_recovery_and_republishes_generated_events()
    {
        var fixture = new Fixture();

        await fixture.Consumer.Consume(new ProcessPaymentWorkCommand
        {
            TenantId = TenantId,
            IncludeRecovery = true
        });

        fixture.Webhooks.Verify(processor => processor.ProcessDueAsync(
            TenantId,
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.PaymentOutbox.Verify(processor => processor.PublishDueAsync(
            TenantId,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.RefundOutbox.Verify(processor => processor.PublishDueAsync(
            TenantId,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.VerifyRecovery(Times.Once());
    }

    private sealed class Fixture
    {
        public Mock<IPaymentWebhookProcessor> Webhooks { get; } = new();
        public Mock<IPaymentOutboxProcessor> PaymentOutbox { get; } = new();
        public Mock<IPaymentRefundOutboxProcessor> RefundOutbox { get; } = new();
        public Mock<IPaymentRecoveryProcessor> PaymentRecovery { get; } = new();
        public Mock<IStoredPaymentMethodRemovalRecoveryProcessor>
            StoredMethodRecovery { get; } = new();
        public Mock<IPaymentRefundRecoveryProcessor> RefundRecovery { get; } = new();
        public PaymentWorkCommandConsumer Consumer { get; }

        public Fixture()
        {
            var contexts = new Mock<IPaymentTenantContextScopeFactory>();
            contexts.Setup(factory => factory.Establish(TenantId))
                .Returns(Mock.Of<IDisposable>());
            Webhooks.Setup(processor => processor.ProcessDueAsync(
                    TenantId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            PaymentOutbox.Setup(processor => processor.PublishDueAsync(
                    TenantId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            RefundOutbox.Setup(processor => processor.PublishDueAsync(
                    TenantId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            PaymentRecovery.Setup(processor => processor.RecoverStaleAsync(
                    TenantId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            StoredMethodRecovery.Setup(processor =>
                    processor.RecoverDueRemovalsAsync(
                        TenantId,
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            RefundRecovery.Setup(processor => processor.RecoverDueAsync(
                    TenantId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            var services = new ServiceCollection();
            services.AddSingleton(contexts.Object);
            services.AddScoped(_ => Webhooks.Object);
            services.AddScoped(_ => PaymentOutbox.Object);
            services.AddScoped(_ => RefundOutbox.Object);
            services.AddScoped(_ => PaymentRecovery.Object);
            services.AddScoped(_ => StoredMethodRecovery.Object);
            services.AddScoped(_ => RefundRecovery.Object);
            var provider = services.BuildServiceProvider();

            Consumer = new PaymentWorkCommandConsumer(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Mock.Of<ILogger<PaymentWorkCommandConsumer>>());
        }

        public void VerifyDueWork(Times times)
        {
            Webhooks.Verify(processor => processor.ProcessDueAsync(
                TenantId,
                It.IsAny<CancellationToken>()), times);
            PaymentOutbox.Verify(processor => processor.PublishDueAsync(
                TenantId,
                It.IsAny<CancellationToken>()), times);
            RefundOutbox.Verify(processor => processor.PublishDueAsync(
                TenantId,
                It.IsAny<CancellationToken>()), times);
        }

        public void VerifyRecovery(Times times)
        {
            PaymentRecovery.Verify(processor => processor.RecoverStaleAsync(
                TenantId,
                It.IsAny<CancellationToken>()), times);
            StoredMethodRecovery.Verify(processor =>
                processor.RecoverDueRemovalsAsync(
                    TenantId,
                    It.IsAny<CancellationToken>()), times);
            RefundRecovery.Verify(processor => processor.RecoverDueAsync(
                TenantId,
                It.IsAny<CancellationToken>()), times);
        }
    }
}
