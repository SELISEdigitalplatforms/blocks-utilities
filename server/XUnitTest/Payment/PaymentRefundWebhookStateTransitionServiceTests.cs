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
    // Providers name a successful refund differently. Matching only Adyen's REFUND meant a
    // Stripe refund reached here, matched nothing, and was skipped as unrecognised, leaving
    // the refund submitted forever with the money already returned.
    [InlineData(
        "refund.created",
        true,
        PaymentRefundStatuses.Submitted,
        PaymentRefundStatuses.Succeeded,
        -10,
        10)]
    [InlineData(
        "refund.updated",
        true,
        PaymentRefundStatuses.Submitted,
        PaymentRefundStatuses.Succeeded,
        -10,
        10)]
    [InlineData(
        "charge.refund.updated",
        true,
        PaymentRefundStatuses.Submitted,
        PaymentRefundStatuses.Succeeded,
        -10,
        10)]
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
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
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
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<PaymentOutboxEvent>(),
                    It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Incomplete_payload_is_rejected()
    {
        var fixture = new Fixture(PaymentRefundStatuses.Submitted);
        var webhook = fixture.Webhook("REFUND", true);
        webhook.NormalizedPayload.RefundId = null;

        var act = () => fixture.Service.ApplyAsync(webhook, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Unknown_payment_detail_is_rejected()
    {
        var fixture = new Fixture(PaymentRefundStatuses.Submitted);
        var webhook = fixture.Webhook("REFUND", true);
        webhook.NormalizedPayload.PaymentDetailId = "mismatched-payment";

        var act = () => fixture.Service.ApplyAsync(webhook, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Mismatched_original_reference_is_rejected()
    {
        var fixture = new Fixture(PaymentRefundStatuses.Submitted);
        var webhook = fixture.Webhook("REFUND", true);
        webhook.NormalizedPayload.OriginalPspReference = "different-original";

        var act = () => fixture.Service.ApplyAsync(webhook, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Unrecognized_event_is_skipped_without_state_change()
    {
        var fixture = new Fixture(PaymentRefundStatuses.Submitted);
        var webhook = fixture.Webhook("SOMETHING_ELSE", true);

        await fixture.Service.ApplyAsync(webhook, CancellationToken.None);

        fixture.VerifyNoApply();
    }

    [Fact]
    public async Task Failure_event_for_already_reversed_refund_is_skipped()
    {
        var fixture = new Fixture(PaymentRefundStatuses.Reversed);
        var webhook = fixture.Webhook("REFUND_FAILED", false);

        await fixture.Service.ApplyAsync(webhook, CancellationToken.None);

        fixture.VerifyNoApply();
    }

    [Fact]
    public async Task Cancel_or_refund_reversal_failure_transitions_to_failed()
    {
        var fixture = new Fixture(PaymentRefundStatuses.Submitted);
        fixture.Refund.ProviderOperation = PaymentFundReturnOperations.Reversal;
        var webhook = fixture.Webhook("CANCEL_OR_REFUND", false);

        await fixture.Service.ApplyAsync(webhook, CancellationToken.None);

        fixture.VerifyApply(PaymentRefundStatuses.Failed);
    }

    [Fact]
    public async Task Cancel_or_refund_reversal_cancels_when_provider_action_is_cancel()
    {
        var fixture = new Fixture(PaymentRefundStatuses.Submitted);
        fixture.Refund.ProviderOperation = PaymentFundReturnOperations.Reversal;
        var webhook = fixture.Webhook("CANCEL_OR_REFUND", true);
        webhook.NormalizedPayload.ModificationAction = "cancel";

        await fixture.Service.ApplyAsync(webhook, CancellationToken.None);

        fixture.VerifyApply(PaymentRefundStatuses.Succeeded);
    }

    [Fact]
    public async Task Cancel_or_refund_reversal_refunds_when_provider_action_is_refund()
    {
        var fixture = new Fixture(PaymentRefundStatuses.Submitted);
        fixture.Refund.ProviderOperation = PaymentFundReturnOperations.Reversal;
        var webhook = fixture.Webhook("CANCEL_OR_REFUND", true);
        webhook.NormalizedPayload.ModificationAction = "refund";

        await fixture.Service.ApplyAsync(webhook, CancellationToken.None);

        fixture.VerifyApply(PaymentRefundStatuses.Succeeded);
    }

    [Fact]
    public async Task Cancel_or_refund_reversal_defaults_action_from_captured_amount()
    {
        var fixture = new Fixture(PaymentRefundStatuses.Submitted);
        fixture.Refund.ProviderOperation = PaymentFundReturnOperations.Reversal;
        fixture.Payment.CapturedAmount = 0;
        var webhook = fixture.Webhook("CANCEL_OR_REFUND", true);
        webhook.NormalizedPayload.ModificationAction = null;

        await fixture.Service.ApplyAsync(webhook, CancellationToken.None);

        fixture.VerifyApply(PaymentRefundStatuses.Succeeded);
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
                CapturedAmount = 20,
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
                        It.IsAny<string>(),
                        It.IsAny<string?>(),
                        It.IsAny<string?>(),
                        It.IsAny<string?>(),
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

        public void VerifyNoApply() =>
            Refunds.Verify(repository => repository.ApplyProviderEventAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<decimal>(),
                    It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()),
                Times.Never);

        public void VerifyApply(string targetStatus) =>
            Refunds.Verify(repository => repository.ApplyProviderEventAsync(
                    TenantId, Payment.ItemId, Refund.RefundId,
                    It.IsAny<IReadOnlyCollection<string>>(), targetStatus,
                    It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<decimal>(),
                    It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()),
                Times.Once);

        private delegate void TryConvertCallback(
            decimal amount,
            string currency,
            out long value);
    }
}
