using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentRefundWebhookStateTransitionServiceTests
{
    [Theory]
    [InlineData(
        "REFUND",
        true,
        PaymentRefundStatuses.Submitted,
        PaymentRefundStatuses.Succeeded,
        -10,
        10)]
    [InlineData(
        "REFUND_FAILED",
        false,
        PaymentRefundStatuses.Submitted,
        PaymentRefundStatuses.Failed,
        -10,
        0)]
    [InlineData(
        "REFUND_FAILED",
        false,
        PaymentRefundStatuses.Succeeded,
        PaymentRefundStatuses.Failed,
        0,
        -10)]
    [InlineData(
        "REFUNDED_REVERSED",
        true,
        PaymentRefundStatuses.Succeeded,
        PaymentRefundStatuses.Reversed,
        0,
        -10)]
    public async Task Provider_event_applies_expected_atomic_amount_transition(
        string eventCode,
        bool success,
        string currentStatus,
        string targetStatus,
        decimal reservedDelta,
        decimal refundedDelta)
    {
        var fixture = new Fixture(currentStatus);
        var webhook = fixture.Webhook(
            eventCode,
            success);

        await fixture.Service.ApplyAsync(
            webhook,
            CancellationToken.None);

        fixture.Refunds.Verify(repository =>
            repository.ApplyProviderEventAsync(
                fixture.TenantId,
                fixture.Payment.ItemId,
                fixture.Refund.RefundId,
                It.Is<IReadOnlyCollection<string>>(
                    statuses =>
                        statuses.Contains(currentStatus)),
                targetStatus,
                "refund-psp",
                webhook.EventDateUtc,
                reservedDelta,
                refundedDelta,
                It.Is<PaymentOutboxEvent>(
                    outboxEvent =>
                        outboxEvent.Payload.RefundId ==
                        fixture.Refund.RefundId &&
                        outboxEvent.Payload
                            .RefundAmount == 10),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Amount_mismatch_is_rejected_before_state_change()
    {
        var fixture =
            new Fixture(
                PaymentRefundStatuses.Submitted);
        var webhook = fixture.Webhook(
            "REFUND",
            true);
        webhook.NormalizedPayload.AmountMinorUnits =
            999;

        var act = () => fixture.Service.ApplyAsync(
            webhook,
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>();
        fixture.Refunds.Verify(repository =>
                repository.ApplyProviderEventAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<decimal>(),
                    It.IsAny<decimal>(),
                    It.IsAny<PaymentOutboxEvent>(),
                    It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class Fixture
    {
        public string TenantId { get; } =
            "de9fc4f4baa4c4cbc829b6059b372dc6";

        public PaymentRefund Refund { get; }

        public PaymentDetail Payment { get; }

        public Mock<IPaymentRefundRepository> Refunds
        {
            get;
        } = new();

        public PaymentRefundWebhookStateTransitionService
            Service { get; }

        public Fixture(string refundStatus)
        {
            Refund = new PaymentRefund
            {
                RefundId = Guid.NewGuid().ToString(),
                Status = refundStatus,
                Amount = 10,
                CurrencyCode = "EUR",
                OriginalPaymentPspReference =
                    "original-psp",
                ProviderName =
                    PaymentConstants.AdyenOnlineProvider,
                CorrelationId = "correlation"
            };
            Payment = new PaymentDetail
            {
                ItemId = Guid.NewGuid().ToString(),
                TenantId = TenantId,
                OrderId = "order",
                PreciseAmount = 20,
                CurrencyCode = "EUR",
                Refunds = [Refund]
            };

            Refunds.Setup(repository =>
                    repository
                        .GetPaymentByRefundIdAsync(
                            TenantId,
                            Refund.RefundId,
                            It.IsAny<CancellationToken>()))
                .ReturnsAsync(Payment);
            Refunds.Setup(repository =>
                    repository.ApplyProviderEventAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<
                            IReadOnlyCollection<string>>(),
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<DateTime>(),
                        It.IsAny<decimal>(),
                        It.IsAny<decimal>(),
                        It.IsAny<PaymentOutboxEvent>(),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            var minorUnits =
                new Mock<ICurrencyMinorUnitResolver>();
            minorUnits.Setup(resolver =>
                    resolver.TryConvert(
                        10,
                        "EUR",
                        out It.Ref<long>.IsAny))
                .Callback(
                    new TryConvertCallback(
                        (
                            decimal _,
                            string _,
                            out long value) =>
                            value = 1000))
                .Returns(true);

            Service =
                new PaymentRefundWebhookStateTransitionService(
                    Refunds.Object,
                    minorUnits.Object,
                    new PaymentRefundOutboxEventFactory(),
                    NullLogger<
                        PaymentRefundWebhookStateTransitionService>
                        .Instance);
        }

        public PaymentWebhookInbox Webhook(
            string eventCode,
            bool success) =>
            new()
            {
                TenantId = TenantId,
                EventCode = eventCode,
                EventDateUtc = DateTime.UtcNow,
                NormalizedPayload =
                    new PaymentWebhookPayload
                    {
                        PaymentDetailId =
                            Payment.ItemId,
                        RefundId =
                            Refund.RefundId,
                        PspReference =
                            "refund-psp",
                        OriginalPspReference =
                            "original-psp",
                        Success = success,
                        AmountMinorUnits = 1000,
                        CurrencyCode = "EUR"
                    }
            };

        private delegate void TryConvertCallback(
            decimal amount,
            string currency,
            out long value);
    }
}
