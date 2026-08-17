using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using XUnitTest.Payment;

namespace XUnitTest.Integration;

[Collection(MongoIntegrationCollection.Name)]
public sealed class PaymentCaptureRepositoryIntegrationTests
{
    private readonly PaymentRepository _payments;
    private readonly PaymentCaptureRepository _repository;

    public PaymentCaptureRepositoryIntegrationTests(MongoIntegrationFixture fixture)
    {
        _payments = new PaymentRepository(fixture.DbContextProvider,
            TestPaymentOptions.Monitor());
        _repository = new PaymentCaptureRepository(fixture.DbContextProvider);
    }

    private async Task<PaymentDetail> AuthorizedPaymentAsync(string tenantId)
    {
        var payment = new PaymentDetail
        {
            ItemId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            CurrencyCode = "EUR",
            PaymentStatus = PaymentStatuses.Authorized,
            AuthorizedAmount = 100m,
            CapturedAmount = 0m,
            ReservedCaptureAmount = 0m,
            IdempotencyKey = Guid.NewGuid().ToString()
        };
        await _payments.TryCreateAsync(payment, CancellationToken.None);
        return payment;
    }

    private static PaymentCapture NewCapture(decimal amount = 50m) => new()
    {
        CaptureId = Guid.NewGuid().ToString(),
        IdempotencyKey = Guid.NewGuid().ToString(),
        Status = PaymentCaptureStatuses.Initiating,
        Amount = amount,
        CurrencyCode = "EUR",
        ProviderName = "adyen"
    };

    private static PaymentOutboxEvent NewOutboxEvent() => new()
    {
        EventId = Guid.NewGuid().ToString(),
        DeduplicationKey = Guid.NewGuid().ToString(),
        Status = PaymentOutboxStatus.Pending
    };

    [Fact]
    public async Task TryReserve_pushes_capture_and_reserves_amount()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await AuthorizedPaymentAsync(tenantId);
        var capture = NewCapture(40m);

        var reserved = await _repository.TryReserveAsync(
            tenantId, payment.ItemId, capture, 5, CancellationToken.None);

        reserved.Should().BeTrue();
        var stored = await _repository.GetPaymentAsync(tenantId, payment.ItemId, CancellationToken.None);
        stored!.Captures.Should().ContainSingle(c => c.CaptureId == capture.CaptureId);
        stored.ReservedCaptureAmount.Should().Be(40m);
    }

    [Fact]
    public async Task TryReserve_rejects_amount_over_available()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await AuthorizedPaymentAsync(tenantId);

        var reserved = await _repository.TryReserveAsync(
            tenantId, payment.ItemId, NewCapture(500m), 5, CancellationToken.None);

        reserved.Should().BeFalse();
    }

    [Fact]
    public async Task GetPaymentByCaptureId_and_idempotency_key_resolve_payment()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await AuthorizedPaymentAsync(tenantId);
        var capture = NewCapture();
        await _repository.TryReserveAsync(tenantId, payment.ItemId, capture, 5, CancellationToken.None);

        (await _repository.GetPaymentByCaptureIdAsync(tenantId, capture.CaptureId, CancellationToken.None))!
            .ItemId.Should().Be(payment.ItemId);
        (await _repository.GetPaymentByIdempotencyKeyAsync(tenantId, capture.IdempotencyKey, CancellationToken.None))!
            .ItemId.Should().Be(payment.ItemId);
    }

    [Fact]
    public async Task Claim_then_complete_submission_reflects_state()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await AuthorizedPaymentAsync(tenantId);
        var capture = NewCapture();
        await _repository.TryReserveAsync(tenantId, payment.ItemId, capture, 5, CancellationToken.None);

        var leaseId = Guid.NewGuid().ToString();
        var claimed = await _repository.TryClaimInitiationAsync(
            tenantId, payment.ItemId, capture.CaptureId, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);
        claimed.Should().NotBeNull();
        claimed!.InitiationAttemptCount.Should().Be(1);

        var ok = await _repository.CompleteSubmissionAsync(
            tenantId, payment.ItemId, capture.CaptureId, leaseId, "prov-ref",
            "received", NewOutboxEvent(), CancellationToken.None);
        ok.Should().BeTrue();

        var stored = await _repository.GetPaymentAsync(tenantId, payment.ItemId, CancellationToken.None);
        var storedCapture = stored!.Captures.Single();
        storedCapture.Status.Should().Be(PaymentCaptureStatuses.Submitted);
        storedCapture.ProviderCaptureReference.Should().Be("prov-ref");
        stored.OutboxEvents.Should().HaveCount(1);
    }

    [Fact]
    public async Task CompleteRejection_marks_failed_and_releases_reservation()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await AuthorizedPaymentAsync(tenantId);
        var capture = NewCapture(30m);
        await _repository.TryReserveAsync(tenantId, payment.ItemId, capture, 5, CancellationToken.None);
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimInitiationAsync(
            tenantId, payment.ItemId, capture.CaptureId, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        var ok = await _repository.CompleteRejectionAsync(
            tenantId, payment.ItemId, capture.CaptureId, leaseId, 30m, "declined",
            NewOutboxEvent(), CancellationToken.None);

        ok.Should().BeTrue();
        var stored = await _repository.GetPaymentAsync(tenantId, payment.ItemId, CancellationToken.None);
        stored!.Captures.Single().Status.Should().Be(PaymentCaptureStatuses.Failed);
        stored.ReservedCaptureAmount.Should().Be(0m);
    }

    [Fact]
    public async Task MarkInitiationUnknown_and_RequiresAttention_update_status()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await AuthorizedPaymentAsync(tenantId);
        var capture = NewCapture();
        await _repository.TryReserveAsync(tenantId, payment.ItemId, capture, 5, CancellationToken.None);
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimInitiationAsync(
            tenantId, payment.ItemId, capture.CaptureId, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        await _repository.MarkInitiationUnknownAsync(
            tenantId, payment.ItemId, capture.CaptureId, leaseId, "timeout",
            DateTime.UtcNow.AddMinutes(10), CancellationToken.None);
        var afterUnknown = await _repository.GetPaymentAsync(tenantId, payment.ItemId, CancellationToken.None);
        afterUnknown!.Captures.Single().Status.Should().Be(PaymentCaptureStatuses.InitiationUnknown);

        await _repository.MarkRequiresAttentionAsync(
            tenantId, payment.ItemId, capture.CaptureId, null, "manual-review", CancellationToken.None);
        var afterAttention = await _repository.GetPaymentAsync(tenantId, payment.ItemId, CancellationToken.None);
        afterAttention!.Captures.Single().Status.Should().Be(PaymentCaptureStatuses.RequiresAttention);
    }

    [Fact]
    public async Task GetPaymentsWithDueCaptureInitiations_returns_due_work()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await AuthorizedPaymentAsync(tenantId);
        var capture = NewCapture();
        capture.NextRecoveryAttemptAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repository.TryReserveAsync(tenantId, payment.ItemId, capture, 5, CancellationToken.None);

        var due = await _repository.GetPaymentsWithDueCaptureInitiationsAsync(
            tenantId, DateTime.UtcNow, 50, CancellationToken.None);

        due.Should().Contain(p => p.ItemId == payment.ItemId);
    }

    [Fact]
    public async Task ApplyProviderEvent_settles_capture_and_payment()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = await AuthorizedPaymentAsync(tenantId);
        var capture = NewCapture(100m);
        await _repository.TryReserveAsync(tenantId, payment.ItemId, capture, 5, CancellationToken.None);
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimInitiationAsync(
            tenantId, payment.ItemId, capture.CaptureId, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);
        await _repository.CompleteSubmissionAsync(
            tenantId, payment.ItemId, capture.CaptureId, leaseId, "prov-ref",
            "received", NewOutboxEvent(), CancellationToken.None);

        var ok = await _repository.ApplyProviderEventAsync(
            tenantId, payment.ItemId, capture.CaptureId,
            [PaymentCaptureStatuses.Submitted], PaymentCaptureStatuses.Succeeded,
            PaymentStatuses.Captured, "prov-ref", DateTime.UtcNow,
            reservedAmountDelta: -100m, capturedAmountDelta: 100m, null,
            NewOutboxEvent(), CancellationToken.None);

        ok.Should().BeTrue();
        var stored = await _repository.GetPaymentAsync(tenantId, payment.ItemId, CancellationToken.None);
        stored!.PaymentStatus.Should().Be(PaymentStatuses.Captured);
        stored.CapturedAmount.Should().Be(100m);
        stored.Captures.Single().Status.Should().Be(PaymentCaptureStatuses.Succeeded);
    }
}
