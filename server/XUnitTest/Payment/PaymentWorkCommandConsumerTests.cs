using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Payment.DomainService.Commands;
using Payment.DomainService.Outbox;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Outbox;
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


    /// <summary>
    /// The point of the whole correlation envelope: the request that scheduled the work and the
    /// run that performs it share one identifier.
    /// </summary>
    /// <remarks>
    /// Observed from inside the work itself rather than from the log output, because that is
    /// where it has to hold — every processor the consumer calls reads the ambient value to
    /// stamp its own scope, so if it is not established by the time they run, nothing they log
    /// is correlated either.
    /// </remarks>
    [Fact]
    public async Task Consume_runs_the_work_under_the_dispatchers_correlation()
    {
        var fixture = new Fixture();
        string? observed = null;
        fixture.Webhooks
            .Setup(processor => processor.ProcessDueAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => observed = PaymentCorrelation.Current)
            .ReturnsAsync(0);

        await fixture.Consumer.Consume(new ProcessPaymentWorkCommand
        {
            TenantId = TenantId,
            CorrelationId = "trace-from-the-api"
        });

        observed.Should().Be("trace-from-the-api");
    }

    /// <summary>
    /// Commands enqueued before the field existed carry no correlation. They get a synthetic id
    /// that says so, rather than an anonymous GUID that looks like a real trace, and rather than
    /// nothing at all — the run still has to be followable end to end.
    /// </summary>
    [Fact]
    public async Task Consume_marks_a_command_that_carries_no_correlation()
    {
        var fixture = new Fixture();
        string? observed = null;
        fixture.Webhooks
            .Setup(processor => processor.ProcessDueAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => observed = PaymentCorrelation.Current)
            .ReturnsAsync(0);

        await fixture.Consumer.Consume(new ProcessPaymentWorkCommand
        {
            TenantId = TenantId
        });

        observed.Should().StartWith("uncorrelated-");
    }

    /// <summary>
    /// The consumer must not leave its correlation behind on the pooled thread, or the next
    /// command to run on it would be filed under the previous command's identity.
    /// </summary>
    [Fact]
    public async Task Consume_does_not_leak_its_correlation_to_the_next_command()
    {
        var fixture = new Fixture();

        await fixture.Consumer.Consume(new ProcessPaymentWorkCommand
        {
            TenantId = TenantId,
            CorrelationId = "trace-1"
        });

        PaymentCorrelation.Current.Should().Be("none");
    }

    /// <summary>
    /// A failure has to leave a record here. Rethrowing is what lets the queue retry, but on its
    /// own it leaves no trace in this service's own logs.
    /// </summary>
    [Fact]
    public async Task Consume_rethrows_so_the_queue_can_retry()
    {
        var fixture = new Fixture();
        fixture.Webhooks
            .Setup(processor => processor.ProcessDueAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider unreachable"));

        var act = async () => await fixture.Consumer.Consume(
            new ProcessPaymentWorkCommand { TenantId = TenantId });

        await act.Should().ThrowAsync<InvalidOperationException>();
        PaymentCorrelation.Current.Should().Be("none");
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
        public Mock<IPaymentCaptureRecoveryProcessor> CaptureRecovery { get; } = new();
        public Mock<ISubscriptionActivationProcessor> SubscriptionActivation { get; } = new();
        public Mock<ISubscriptionOutboxProcessor> SubscriptionOutbox { get; } = new();
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
            CaptureRecovery.Setup(processor => processor.RecoverDueAsync(
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
            services.AddScoped(_ => CaptureRecovery.Object);
            // Subscriptions ride this same tick, so the consumer resolves their processors too.
            services.AddScoped(_ => SubscriptionActivation.Object);
            services.AddScoped(_ => SubscriptionOutbox.Object);
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
            CaptureRecovery.Verify(processor => processor.RecoverDueAsync(
                TenantId,
                It.IsAny<CancellationToken>()), times);
        }
    }
}
