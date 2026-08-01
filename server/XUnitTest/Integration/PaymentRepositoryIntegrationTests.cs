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

        (await _repository.GetProviderAsync(tenantId, null, "adyen", CancellationToken.None))
            .Should().NotBeNull();

        var disabled = new PaymentProvider
        {
            ItemId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            ProviderName = "stripe-" + Guid.NewGuid().ToString("N"),
            IsEnabled = false
        };
        await _fixture.Collection<PaymentProvider>("PaymentProviders").InsertOneAsync(disabled);
        (await _repository.GetProviderAsync(tenantId, null, disabled.ProviderName, CancellationToken.None))
            .Should().BeNull();
    }

    /// <summary>
    /// Organizations within a tenant may be separate businesses with their own merchant
    /// accounts, so each must reach its own configuration. Returning whichever the database
    /// happened to yield first meant money could settle into the wrong account.
    /// </summary>
    [Fact]
    public async Task Each_organization_resolves_its_own_configuration()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var providerName = "stripe-" + Guid.NewGuid().ToString("N");
        var providers = _fixture.Collection<PaymentProvider>("PaymentProviders");

        await providers.InsertOneAsync(new PaymentProvider
        {
            ItemId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            OrganizationId = "organization-1",
            ProviderName = providerName,
            MerchantId = "merchant-1",
            IsEnabled = true
        });
        await providers.InsertOneAsync(new PaymentProvider
        {
            ItemId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            OrganizationId = "organization-2",
            ProviderName = providerName,
            MerchantId = "merchant-2",
            IsEnabled = true
        });

        (await _repository.GetProviderAsync(
                tenantId, "organization-1", providerName, CancellationToken.None))!
            .MerchantId.Should().Be("merchant-1");
        (await _repository.GetProviderAsync(
                tenantId, "organization-2", providerName, CancellationToken.None))!
            .MerchantId.Should().Be("merchant-2");
    }

    /// <summary>
    /// An organization without its own configuration uses the tenant's, which is what every
    /// configuration registered before organization scoping is.
    /// </summary>
    [Fact]
    public async Task An_organization_without_its_own_configuration_uses_the_tenants()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var providerName = "stripe-" + Guid.NewGuid().ToString("N");

        await _fixture.Collection<PaymentProvider>("PaymentProviders").InsertOneAsync(
            new PaymentProvider
            {
                ItemId = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                OrganizationId = null,
                ProviderName = providerName,
                MerchantId = "tenant-merchant",
                IsEnabled = true
            });

        (await _repository.GetProviderAsync(
                tenantId, "organization-with-none", providerName, CancellationToken.None))!
            .MerchantId.Should().Be("tenant-merchant");
    }

    /// <summary>
    /// Uniqueness includes the organization, so two organizations may each register a
    /// configuration for the same provider and merchant without colliding.
    /// </summary>
    [Fact]
    public async Task Two_organizations_may_register_the_same_provider_and_merchant()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _repository.EnsureIndexesAsync(tenantId, CancellationToken.None);
        var providerName = "stripe-" + Guid.NewGuid().ToString("N");

        PaymentProvider Configuration(string organizationId) => new()
        {
            ItemId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            OrganizationId = organizationId,
            ProviderName = providerName,
            MerchantId = "shared-merchant",
            IsEnabled = true
        };

        (await _repository.TryCreateProviderAsync(
            Configuration("organization-1"), CancellationToken.None)).Should().BeTrue();
        (await _repository.TryCreateProviderAsync(
            Configuration("organization-2"), CancellationToken.None)).Should().BeTrue();

        // The same organization twice is still a duplicate.
        (await _repository.TryCreateProviderAsync(
            Configuration("organization-1"), CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Provider_configuration_compare_and_set_allows_only_one_concurrent_update()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var provider = new PaymentProvider
        {
            ItemId = Guid.NewGuid().ToString(),
            Version = 1,
            TenantId = tenantId,
            ProviderName = "ADYEN-ONLINE",
            MerchantId = "merchant-1",
            ApiBaseUrl =
                "https://checkout-test.adyen.com/v72",
            FrontendResultUrl =
                "https://client.example/original",
            IsEnabled = true
        };
        await _fixture.Collection<PaymentProvider>(
                "PaymentProviders")
            .InsertOneAsync(provider);

        var first = _repository
            .TryUpdateProviderConfigurationAsync(
                tenantId,
                provider.ItemId,
                1,
                "https://client.example/first",
                "CH",
                false,
                90,
                null,
                true,
                CancellationToken.None);
        var second = _repository
            .TryUpdateProviderConfigurationAsync(
                tenantId,
                provider.ItemId,
                1,
                "https://client.example/second",
                "CH",
                false,
                90,
                null,
                true,
                CancellationToken.None);

        var results = await Task.WhenAll(first, second);

        results.Count(result => result != null).Should().Be(1);
        var stored = await _repository.GetProviderByIdAsync(
            tenantId,
            provider.ItemId,
            CancellationToken.None);
        stored!.Version.Should().Be(2);
        stored.FrontendResultUrl.Should().BeOneOf(
            "https://client.example/first",
            "https://client.example/second");
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
