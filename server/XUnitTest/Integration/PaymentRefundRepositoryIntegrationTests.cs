using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;

namespace XUnitTest.Integration;

[Collection(MongoIntegrationCollection.Name)]
public sealed class PaymentRefundRepositoryIntegrationTests
{
    private readonly PaymentRepository _payments;
    private readonly PaymentRefundRepository _repository;

    public PaymentRefundRepositoryIntegrationTests(MongoIntegrationFixture fixture)
    {
        _payments = new PaymentRepository(fixture.DbContextProvider);
        _repository = new PaymentRefundRepository(fixture.DbContextProvider);
    }

    private async Task<PaymentDetail> CapturedPaymentAsync(string tenantId, decimal captured = 100m)
    {
        var payment = new PaymentDetail
        {
            ItemId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            CurrencyCode = "EUR",
            PaymentStatus = PaymentStatuses.Captured,
            PreciseAmount = captured,
            CapturedAmount = captured,
            RefundedAmount = 0m,
            ReservedRefundAmount = 0m,
            IdempotencyKey = Guid.NewGuid().ToString()
        };
        await _payments.TryCreateAsync(payment, CancellationToken.None);
        return payment;
    }

    private static PaymentRefund NewRefund(decimal amount = 40m) => new()
    {
        RefundId = Guid.NewGuid().ToString(),
        IdempotencyKey = Guid.NewGuid().ToString(),
        Status = PaymentRefundStatuses.Initiating,
        Amount = amount,
        CurrencyCode = "EUR",
        ProviderName = "adyen",
        ProviderOperation = PaymentFundReturnOperations.Refund,
        NextRecoveryAttemptAtUtc = DateTime.UtcNow.AddMinutes(-5)
    };

    private static PaymentOutboxEvent NewOutboxEvent() => new()
    {
        EventId = Guid.NewGuid().ToString(),
        DeduplicationKey = Guid.NewGuid().ToString(),
        Status = PaymentOutboxStatus.Pending,
        NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1)
    };

    [Fact]
    public async Task TryReserve_pushes_refund_and_reserves_amount()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await CapturedPaymentAsync(tenantId);
        var refund = NewRefund(40m);

        var reserved = await _repository.TryReserveAsync(
            tenantId, payment.ItemId, refund, 5, CancellationToken.None);

        reserved.Should().BeTrue();
        var stored = await _repository.GetPaymentAsync(tenantId, payment.ItemId, CancellationToken.None);
        stored!.Refunds.Should().ContainSingle(r => r.RefundId == refund.RefundId);
        stored.ReservedRefundAmount.Should().Be(40m);
    }

    [Fact]
    public async Task TryReserve_rejects_amount_over_available()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await CapturedPaymentAsync(tenantId, 100m);

        (await _repository.TryReserveAsync(
            tenantId, payment.ItemId, NewRefund(500m), 5, CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task TryReserve_reversal_requires_full_untouched_amount()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await CapturedPaymentAsync(tenantId, 100m);
        var reversal = NewRefund(100m);
        reversal.ProviderOperation = PaymentFundReturnOperations.Reversal;

        (await _repository.TryReserveAsync(
            tenantId, payment.ItemId, reversal, 5, CancellationToken.None))
            .Should().BeTrue();
    }

    [Fact]
    public async Task GetPaymentByRefundId_and_idempotency_key_resolve_payment()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await CapturedPaymentAsync(tenantId);
        var refund = NewRefund();
        await _repository.TryReserveAsync(tenantId, payment.ItemId, refund, 5, CancellationToken.None);

        (await _repository.GetPaymentByRefundIdAsync(tenantId, refund.RefundId, CancellationToken.None))!
            .ItemId.Should().Be(payment.ItemId);
        (await _repository.GetPaymentByRefundIdempotencyKeyAsync(
            tenantId, refund.IdempotencyKey, CancellationToken.None))!
            .ItemId.Should().Be(payment.ItemId);
    }

    [Fact]
    public async Task Claim_then_complete_submission_reflects_state()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await CapturedPaymentAsync(tenantId);
        var refund = NewRefund();
        await _repository.TryReserveAsync(tenantId, payment.ItemId, refund, 5, CancellationToken.None);

        var leaseId = Guid.NewGuid().ToString();
        var claimed = await _repository.TryClaimInitiationAsync(
            tenantId, payment.ItemId, refund.RefundId, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);
        claimed.Should().NotBeNull();
        claimed!.InitiationAttemptCount.Should().Be(1);

        var ok = await _repository.CompleteSubmissionAsync(
            tenantId, payment.ItemId, refund.RefundId, leaseId, "prov-ref",
            "received", NewOutboxEvent(), CancellationToken.None);
        ok.Should().BeTrue();

        var stored = await _repository.GetPaymentAsync(tenantId, payment.ItemId, CancellationToken.None);
        var storedRefund = stored!.Refunds.Single();
        storedRefund.Status.Should().Be(PaymentRefundStatuses.Submitted);
        storedRefund.ProviderRefundReference.Should().Be("prov-ref");
        storedRefund.OutboxEvents.Should().HaveCount(1);
    }

    [Fact]
    public async Task CompleteRejection_marks_failed_and_releases_reservation()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await CapturedPaymentAsync(tenantId);
        var refund = NewRefund(30m);
        await _repository.TryReserveAsync(tenantId, payment.ItemId, refund, 5, CancellationToken.None);
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimInitiationAsync(
            tenantId, payment.ItemId, refund.RefundId, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        var ok = await _repository.CompleteRejectionAsync(
            tenantId, payment.ItemId, refund.RefundId, leaseId, 30m, "declined",
            NewOutboxEvent(), CancellationToken.None);

        ok.Should().BeTrue();
        var stored = await _repository.GetPaymentAsync(tenantId, payment.ItemId, CancellationToken.None);
        stored!.Refunds.Single().Status.Should().Be(PaymentRefundStatuses.Failed);
        stored.ReservedRefundAmount.Should().Be(0m);
    }

    [Fact]
    public async Task MarkInitiationUnknown_and_RequiresAttention_update_status()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await CapturedPaymentAsync(tenantId);
        var refund = NewRefund();
        await _repository.TryReserveAsync(tenantId, payment.ItemId, refund, 5, CancellationToken.None);
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimInitiationAsync(
            tenantId, payment.ItemId, refund.RefundId, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        await _repository.MarkInitiationUnknownAsync(
            tenantId, payment.ItemId, refund.RefundId, leaseId, "timeout",
            DateTime.UtcNow.AddMinutes(10), CancellationToken.None);
        (await _repository.GetPaymentAsync(tenantId, payment.ItemId, CancellationToken.None))!
            .Refunds.Single().Status.Should().Be(PaymentRefundStatuses.InitiationUnknown);

        await _repository.MarkRequiresAttentionAsync(
            tenantId, payment.ItemId, refund.RefundId, null, "manual", CancellationToken.None);
        (await _repository.GetPaymentAsync(tenantId, payment.ItemId, CancellationToken.None))!
            .Refunds.Single().Status.Should().Be(PaymentRefundStatuses.RequiresAttention);
    }

    [Fact]
    public async Task ApplyProviderEvent_settles_refund_and_payment()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await CapturedPaymentAsync(tenantId, 100m);
        var refund = NewRefund(100m);
        await _repository.TryReserveAsync(tenantId, payment.ItemId, refund, 5, CancellationToken.None);
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimInitiationAsync(
            tenantId, payment.ItemId, refund.RefundId, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);
        await _repository.CompleteSubmissionAsync(
            tenantId, payment.ItemId, refund.RefundId, leaseId, "prov-ref",
            "received", NewOutboxEvent(), CancellationToken.None);

        var ok = await _repository.ApplyProviderEventAsync(
            tenantId, payment.ItemId, refund.RefundId,
            [PaymentRefundStatuses.Submitted], PaymentRefundStatuses.Succeeded,
            "prov-ref", DateTime.UtcNow, reservedAmountDelta: -100m, refundedAmountDelta: 100m,
            PaymentStatuses.Refunded, null, null, null, NewOutboxEvent(), CancellationToken.None);

        ok.Should().BeTrue();
        var stored = await _repository.GetPaymentAsync(tenantId, payment.ItemId, CancellationToken.None);
        stored!.PaymentStatus.Should().Be(PaymentStatuses.Refunded);
        stored.RefundedAmount.Should().Be(100m);
        stored.Refunds.Single().Status.Should().Be(PaymentRefundStatuses.Succeeded);
    }

    [Fact]
    public async Task GetPaymentsWithDueRefundInitiations_returns_due_work()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await CapturedPaymentAsync(tenantId);
        await _repository.TryReserveAsync(tenantId, payment.ItemId, NewRefund(), 5, CancellationToken.None);

        var due = await _repository.GetPaymentsWithDueRefundInitiationsAsync(
            tenantId, DateTime.UtcNow, 50, CancellationToken.None);

        due.Should().Contain(p => p.ItemId == payment.ItemId);
    }

    [Fact]
    public async Task Refund_outbox_claim_publish_and_fail_lifecycle()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await CapturedPaymentAsync(tenantId);
        var refund = NewRefund();
        await _repository.TryReserveAsync(tenantId, payment.ItemId, refund, 5, CancellationToken.None);
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimInitiationAsync(
            tenantId, payment.ItemId, refund.RefundId, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);
        var outboxEvent = NewOutboxEvent();
        await _repository.CompleteSubmissionAsync(
            tenantId, payment.ItemId, refund.RefundId, leaseId, "prov-ref",
            "received", outboxEvent, CancellationToken.None);

        var due = await _repository.GetPaymentsWithDueRefundOutboxEventsAsync(
            tenantId, DateTime.UtcNow, 50, CancellationToken.None);
        due.Should().Contain(p => p.ItemId == payment.ItemId);

        var eventLease = Guid.NewGuid().ToString();
        var claimedEvent = await _repository.TryClaimOutboxEventAsync(
            tenantId, payment.ItemId, refund.RefundId, outboxEvent.EventId, eventLease,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);
        claimedEvent.Should().BeTrue();

        await _repository.MarkOutboxPublishedAsync(
            tenantId, payment.ItemId, refund.RefundId, outboxEvent.EventId, eventLease,
            DateTime.UtcNow, CancellationToken.None);
        var afterPublish = await _repository.GetPaymentAsync(tenantId, payment.ItemId, CancellationToken.None);
        afterPublish!.Refunds.Single().OutboxEvents.Single().Status
            .Should().Be(PaymentOutboxStatus.Published);
    }

    [Fact]
    public async Task Refund_outbox_MarkFailed_records_retry_state()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await CapturedPaymentAsync(tenantId);
        var refund = NewRefund();
        await _repository.TryReserveAsync(tenantId, payment.ItemId, refund, 5, CancellationToken.None);
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimInitiationAsync(
            tenantId, payment.ItemId, refund.RefundId, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);
        var outboxEvent = NewOutboxEvent();
        await _repository.CompleteSubmissionAsync(
            tenantId, payment.ItemId, refund.RefundId, leaseId, "prov-ref",
            "received", outboxEvent, CancellationToken.None);
        var eventLease = Guid.NewGuid().ToString();
        await _repository.TryClaimOutboxEventAsync(
            tenantId, payment.ItemId, refund.RefundId, outboxEvent.EventId, eventLease,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        await _repository.MarkOutboxFailedAsync(
            tenantId, payment.ItemId, refund.RefundId, outboxEvent.EventId, eventLease,
            PaymentOutboxStatus.RetryScheduled, 2, DateTime.UtcNow.AddMinutes(10), "boom",
            CancellationToken.None);

        var stored = await _repository.GetPaymentAsync(tenantId, payment.ItemId, CancellationToken.None);
        var stateEvent = stored!.Refunds.Single().OutboxEvents.Single();
        stateEvent.Status.Should().Be(PaymentOutboxStatus.RetryScheduled);
        stateEvent.AttemptCount.Should().Be(2);
    }
}
