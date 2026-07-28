using FluentAssertions;
using Payment.DomainService.Models;
using Payment.DomainService.Utilities;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Repositories;

namespace XUnitTest.Integration;

[Collection(MongoIntegrationCollection.Name)]
public sealed class PaymentRepositoryIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly PaymentRepository _repository;

    public PaymentRepositoryIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _repository = new PaymentRepository(fixture.DbContextProvider);
    }

    private static PaymentDetail NewPayment(string tenantId) => new()
    {
        ItemId = Guid.NewGuid().ToString(),
        TenantId = tenantId,
        ProviderName = "adyen",
        CurrencyCode = "EUR",
        PreciseAmount = 100m,
        IdempotencyKey = Guid.NewGuid().ToString(),
        PaymentStatus = PaymentStatuses.Initiating,
        CreatedAtUtc = DateTime.UtcNow,
        LastUpdatedDateUtc = DateTime.UtcNow
    };

    private static PaymentOutboxEvent NewOutboxEvent() => new()
    {
        EventId = Guid.NewGuid().ToString(),
        EventType = "payment.authorised",
        DeduplicationKey = Guid.NewGuid().ToString(),
        Status = PaymentOutboxStatus.Pending,
        NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1)
    };

    [Fact]
    public async Task TryCreate_persists_and_reads_back_the_payment()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = NewPayment(tenantId);

        var created = await _repository.TryCreateAsync(payment, CancellationToken.None);

        created.Should().BeTrue();
        var stored = await _repository.GetByIdAsync(tenantId, payment.ItemId, CancellationToken.None);
        stored.Should().NotBeNull();
        stored!.IdempotencyKey.Should().Be(payment.IdempotencyKey);
    }

    [Fact]
    public async Task TryCreate_returns_false_on_duplicate_id()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = NewPayment(tenantId);
        (await _repository.TryCreateAsync(payment, CancellationToken.None)).Should().BeTrue();

        var duplicate = NewPayment(tenantId);
        duplicate.ItemId = payment.ItemId;

        (await _repository.TryCreateAsync(duplicate, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdempotencyKey_and_PspReference_filter_correctly()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = NewPayment(tenantId);
        payment.PspReference = "psp-" + Guid.NewGuid().ToString("N");
        await _repository.TryCreateAsync(payment, CancellationToken.None);

        (await _repository.GetByIdempotencyKeyAsync(tenantId, payment.IdempotencyKey, CancellationToken.None))
            .Should().NotBeNull();
        (await _repository.GetByPspReferenceAsync(tenantId, payment.PspReference!, CancellationToken.None))!
            .ItemId.Should().Be(payment.ItemId);
        (await _repository.GetByPspReferenceAsync(tenantId, "missing", CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task GetProvider_matches_case_insensitively_when_enabled()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var provider = new PaymentProvider
        {
            ItemId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            ProviderName = "Adyen",
            IsEnabled = true
        };
        await _fixture.Collection<PaymentProvider>("PaymentProviders").InsertOneAsync(provider);

        (await _repository.GetProviderAsync(tenantId, "adyen", CancellationToken.None))
            .Should().NotBeNull();

        var disabled = new PaymentProvider
        {
            ItemId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            ProviderName = "stripe-" + Guid.NewGuid().ToString("N"),
            IsEnabled = false
        };
        await _fixture.Collection<PaymentProvider>("PaymentProviders").InsertOneAsync(disabled);
        (await _repository.GetProviderAsync(tenantId, disabled.ProviderName, CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task GetRecurringPaymentByOrderId_only_matches_recurring_flow()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var orderId = Guid.NewGuid().ToString();
        var recurring = NewPayment(tenantId);
        recurring.PaymentFlow = PaymentFlows.RecurringCharge;
        recurring.OrderId = orderId;
        await _repository.TryCreateAsync(recurring, CancellationToken.None);

        (await _repository.GetRecurringPaymentByOrderIdAsync(tenantId, orderId, CancellationToken.None))!
            .ItemId.Should().Be(recurring.ItemId);
    }

    [Fact]
    public async Task Claim_save_and_complete_initiation_flow_reflects_in_storage()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = NewPayment(tenantId);
        await _repository.TryCreateAsync(payment, CancellationToken.None);

        var leaseId = Guid.NewGuid().ToString();
        var claimed = await _repository.TryClaimInitiationAsync(
            tenantId, payment.ItemId, leaseId, DateTime.UtcNow.AddMinutes(5), CancellationToken.None);
        claimed.Should().NotBeNull();
        claimed!.ProcessingLeaseId.Should().Be(leaseId);
        claimed.InitiationAttemptCount.Should().Be(1);

        var request = new ProviderInitiationRequest
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            Reference = "ref-1",
            MerchantAccount = "merchant-1",
            CaptureMode = PaymentCaptureModes.AutomaticImmediate,
            CaptureDelayHours = 0,
            SiteId = "site-1"
        };
        var saved = await _repository.SaveInitiationRequestAsync(
            tenantId, payment.ItemId, leaseId, request, "https://frontend/return",
            "nonce-hash", "shopper-1", CancellationToken.None);
        saved.Should().BeTrue();

        var completed = await _repository.CompleteInitiationAsync(
            tenantId, payment.ItemId, leaseId, PaymentStatuses.Processing,
            "session-1", "session-data", "https://redirect", DateTime.UtcNow.AddHours(1),
            null, NewOutboxEvent(), CancellationToken.None);
        completed.Should().BeTrue();

        var stored = await _repository.GetByIdAsync(tenantId, payment.ItemId, CancellationToken.None);
        stored!.PaymentStatus.Should().Be(PaymentStatuses.Processing);
        stored.SessionId.Should().Be("session-1");
        stored.ProcessingLeaseId.Should().BeNull();
        stored.OutboxEvents.Should().HaveCount(1);
        stored.ProviderMerchantAccount.Should().Be("merchant-1");
        stored.SiteId.Should().Be("site-1");
    }

    [Fact]
    public async Task MarkInitiationUnknown_sets_status_and_releases_lease()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = NewPayment(tenantId);
        await _repository.TryCreateAsync(payment, CancellationToken.None);
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimInitiationAsync(
            tenantId, payment.ItemId, leaseId, DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        await _repository.MarkInitiationUnknownAsync(
            tenantId, payment.ItemId, leaseId, "provider-timeout", CancellationToken.None);

        var stored = await _repository.GetByIdAsync(tenantId, payment.ItemId, CancellationToken.None);
        stored!.PaymentStatus.Should().Be(PaymentStatuses.InitiationUnknown);
        stored.FailureCode.Should().Be("provider-timeout");
    }

    [Fact]
    public async Task CompleteStoredPaymentChargeInitiation_moves_to_processing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = NewPayment(tenantId);
        await _repository.TryCreateAsync(payment, CancellationToken.None);
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimInitiationAsync(
            tenantId, payment.ItemId, leaseId, DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        var ok = await _repository.CompleteStoredPaymentChargeInitiationAsync(
            tenantId, payment.ItemId, leaseId, "psp-ref", "Authorised",
            NewOutboxEvent(), CancellationToken.None);

        ok.Should().BeTrue();
        var stored = await _repository.GetByIdAsync(tenantId, payment.ItemId, CancellationToken.None);
        stored!.PaymentStatus.Should().Be(PaymentStatuses.Processing);
        stored.PspReference.Should().Be("psp-ref");
    }

    [Fact]
    public async Task SaveProviderRouting_updates_reference_and_account()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = NewPayment(tenantId);
        await _repository.TryCreateAsync(payment, CancellationToken.None);
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimInitiationAsync(
            tenantId, payment.ItemId, leaseId, DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        var ok = await _repository.SaveProviderRoutingAsync(
            tenantId, payment.ItemId, leaseId, "provider-ref", "merchant-acc", CancellationToken.None);

        ok.Should().BeTrue();
        var stored = await _repository.GetByIdAsync(tenantId, payment.ItemId, CancellationToken.None);
        stored!.ProviderReference.Should().Be("provider-ref");
        stored.ProviderMerchantAccount.Should().Be("merchant-acc");
    }

    [Fact]
    public async Task SaveCheckoutObservation_records_session_state()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = NewPayment(tenantId);
        await _repository.TryCreateAsync(payment, CancellationToken.None);

        var ok = await _repository.SaveCheckoutObservationAsync(
            tenantId, payment.ItemId, "completed", "Authorised", "result-hash",
            "psp-obs", null, CancellationToken.None);

        ok.Should().BeTrue();
        var stored = await _repository.GetByIdAsync(tenantId, payment.ItemId, CancellationToken.None);
        stored!.CheckoutSessionStatus.Should().Be("completed");
        stored.SessionResultHash.Should().Be("result-hash");
    }

    [Fact]
    public async Task ApplyAuthorisation_captured_automatically_sets_amounts()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = NewPayment(tenantId);
        await _repository.TryCreateAsync(payment, CancellationToken.None);

        var ok = await _repository.ApplyAuthorisationAsync(
            tenantId, payment.ItemId, authorized: true, authorizedAmount: 100m,
            capturedAutomatically: true, "psp-auth", DateTime.UtcNow, null,
            NewOutboxEvent(), CancellationToken.None);

        ok.Should().BeTrue();
        var stored = await _repository.GetByIdAsync(tenantId, payment.ItemId, CancellationToken.None);
        stored!.PaymentStatus.Should().Be(PaymentStatuses.Captured);
        stored.AuthorizedAmount.Should().Be(100m);
        stored.CapturedAmount.Should().Be(100m);
        stored.CaptureStatus.Should().Be(PaymentCaptureStatuses.Succeeded);
    }

    [Fact]
    public async Task ApplyAuthorisation_refused_sets_refused_status()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = NewPayment(tenantId);
        await _repository.TryCreateAsync(payment, CancellationToken.None);

        await _repository.ApplyAuthorisationAsync(
            tenantId, payment.ItemId, authorized: false, authorizedAmount: 0m,
            capturedAutomatically: false, "psp-ref", DateTime.UtcNow, null,
            NewOutboxEvent(), CancellationToken.None);

        var stored = await _repository.GetByIdAsync(tenantId, payment.ItemId, CancellationToken.None);
        stored!.PaymentStatus.Should().Be(PaymentStatuses.Refused);
    }

    [Fact]
    public async Task Outbox_claim_publish_and_fail_lifecycle()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = NewPayment(tenantId);
        var evt = NewOutboxEvent();
        payment.OutboxEvents.Add(evt);
        await _repository.TryCreateAsync(payment, CancellationToken.None);

        var due = await _repository.GetPaymentsWithDueOutboxEventsAsync(
            tenantId, DateTime.UtcNow, 50, CancellationToken.None);
        due.Should().ContainSingle(p => p.ItemId == payment.ItemId);

        var leaseId = Guid.NewGuid().ToString();
        var claimed = await _repository.TryClaimOutboxEventAsync(
            tenantId, payment.ItemId, evt.EventId, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);
        claimed.Should().BeTrue();

        await _repository.MarkOutboxPublishedAsync(
            tenantId, payment.ItemId, evt.EventId, leaseId, DateTime.UtcNow, CancellationToken.None);
        var afterPublish = await _repository.GetByIdAsync(tenantId, payment.ItemId, CancellationToken.None);
        afterPublish!.OutboxEvents.Single().Status.Should().Be(PaymentOutboxStatus.Published);
    }

    [Fact]
    public async Task MarkOutboxFailed_records_retry_state()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = NewPayment(tenantId);
        var evt = NewOutboxEvent();
        payment.OutboxEvents.Add(evt);
        await _repository.TryCreateAsync(payment, CancellationToken.None);
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimOutboxEventAsync(
            tenantId, payment.ItemId, evt.EventId, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        await _repository.MarkOutboxFailedAsync(
            tenantId, payment.ItemId, evt.EventId, leaseId, PaymentOutboxStatus.RetryScheduled,
            2, DateTime.UtcNow.AddMinutes(10), "boom", CancellationToken.None);

        var stored = await _repository.GetByIdAsync(tenantId, payment.ItemId, CancellationToken.None);
        var stateEvent = stored!.OutboxEvents.Single();
        stateEvent.Status.Should().Be(PaymentOutboxStatus.RetryScheduled);
        stateEvent.AttemptCount.Should().Be(2);
        stateEvent.LastError.Should().Be("boom");
    }

    [Fact]
    public async Task GetStaleInitiations_returns_unleased_initiating_payments()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var payment = NewPayment(tenantId);
        await _repository.TryCreateAsync(payment, CancellationToken.None);

        var stale = await _repository.GetStaleInitiationsAsync(
            tenantId, DateTime.UtcNow, 50, CancellationToken.None);

        stale.Should().Contain(p => p.ItemId == payment.ItemId);
    }

    [Fact]
    public async Task HasUnresolvedRecurringPayment_detects_in_flight_charge()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var storedMethodId = Guid.NewGuid().ToString();
        var payment = NewPayment(tenantId);
        payment.PaymentFlow = PaymentFlows.RecurringCharge;
        payment.StoredPaymentMethodPublicId = storedMethodId;
        payment.PaymentStatus = PaymentStatuses.Processing;
        await _repository.TryCreateAsync(payment, CancellationToken.None);

        (await _repository.HasUnresolvedRecurringPaymentAsync(tenantId, storedMethodId, CancellationToken.None))
            .Should().BeTrue();
        (await _repository.HasUnresolvedRecurringPaymentAsync(tenantId, "other", CancellationToken.None))
            .Should().BeFalse();
    }
}
