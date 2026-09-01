using FluentAssertions;
using Payment.DomainService.Models;
using Payment.DomainService.Utilities;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Repositories;
using XUnitTest.Payment;

namespace XUnitTest.Integration;

[Collection(MongoIntegrationCollection.Name)]
public sealed class PaymentRepositoryIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly PaymentRepository _repository;

    public PaymentRepositoryIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _repository = new PaymentRepository(fixture.DbContextProvider,
            TestPaymentOptions.Monitor());
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
    /// A tenant that configured one merchant account from the console meant it for the tenant,
    /// so its organizations resolve it rather than reporting the provider unavailable.
    /// </summary>
    [Fact]
    public async Task An_organization_falls_back_to_the_configuration_the_console_registered()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var providerName = "stripe-" + Guid.NewGuid().ToString("N");

        await _fixture.Collection<PaymentProvider>("PaymentProviders").InsertOneAsync(
            new PaymentProvider
            {
                ItemId = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                OrganizationId = TestPaymentOptions.ConsoleOrganizationId,
                ProviderName = providerName,
                MerchantId = "console-merchant",
                IsEnabled = true
            });

        var found = await _repository.GetProviderAsync(
            tenantId, "organization-with-none", providerName, CancellationToken.None);

        found!.MerchantId.Should().Be("console-merchant");

        // Returned as stored. Every later encryption scope reads this, so a shared
        // configuration's credentials stay on the key ring they were sealed under.
        found.OrganizationId.Should().Be(TestPaymentOptions.ConsoleOrganizationId);
    }

    /// <summary>
    /// The tenant's own configuration outranks the console's, so nothing a tenant already set
    /// up changes meaning.
    /// </summary>
    [Fact]
    public async Task A_tenant_level_configuration_outranks_the_consoles()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var providerName = "stripe-" + Guid.NewGuid().ToString("N");
        var providers = _fixture.Collection<PaymentProvider>("PaymentProviders");

        await providers.InsertOneAsync(new PaymentProvider
        {
            ItemId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            OrganizationId = null,
            ProviderName = providerName,
            MerchantId = "tenant-merchant",
            IsEnabled = true
        });
        await providers.InsertOneAsync(new PaymentProvider
        {
            ItemId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            OrganizationId = TestPaymentOptions.ConsoleOrganizationId,
            ProviderName = providerName,
            MerchantId = "console-merchant",
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

    private static PaymentDetail NewSetup(string tenantId) => new()
    {
        ItemId = Guid.NewGuid().ToString(),
        TenantId = tenantId,
        ProviderName = "adyen",
        CurrencyCode = "EUR",
        PreciseAmount = 0m,
        IdempotencyKey = Guid.NewGuid().ToString(),
        PaymentFlow = PaymentFlows.PaymentMethodSetup,
        PaymentStatus = PaymentStatuses.Processing,
        CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
        LastUpdatedDateUtc = DateTime.UtcNow.AddHours(-1)
    };

    /// <summary>
    /// PR #393 review, Finding 1: the expiry sweep's final compare-and-set must re-verify "still
    /// missing a signal" atomically in the same write as the status flip, not only at candidate
    /// selection. This reproduces the race directly against Mongo: a setup is read as a candidate
    /// (missing its token signal), then -- simulating the token webhook winning the race -- the
    /// signal is recorded before the expiry attempt runs. The stale candidate read must not be
    /// enough to expire it.
    /// </summary>
    [Fact]
    public async Task TryExpireSetup_loses_the_race_to_a_signal_recorded_after_the_candidate_was_read()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var setup = NewSetup(tenantId);
        setup.SetupAuthorizationConfirmedAtUtc = DateTime.UtcNow.AddHours(-1);
        await _repository.TryCreateAsync(setup, CancellationToken.None);

        var candidates = await _repository.GetDueSetupExpiryCandidatesAsync(
            tenantId, DateTime.UtcNow, 10, CancellationToken.None);
        candidates.Should().ContainSingle(p => p.ItemId == setup.ItemId);

        // The token webhook wins the race between candidate selection and the expiry attempt.
        (await _repository.TryRecordSetupTokenConfirmedAsync(
                tenantId, setup.ItemId, DateTime.UtcNow, CancellationToken.None))
            .Should().BeTrue();

        var expired = await _repository.TryExpireSetupAsync(
            tenantId, setup.ItemId, DateTime.UtcNow, CancellationToken.None);

        expired.Should().BeFalse("the setup now has both signals and must not be expired out from under the webhook that just completed it");
        var stored = await _repository.GetByIdAsync(tenantId, setup.ItemId, CancellationToken.None);
        stored!.PaymentStatus.Should().Be(PaymentStatuses.Processing);
        stored.SetupTokenConfirmedAtUtc.Should().NotBeNull();
    }

    /// <summary>
    /// The companion case: a setup genuinely still missing a signal is expired normally, so
    /// Finding 1's extra CAS condition does not silently disable the sweep altogether.
    /// </summary>
    [Fact]
    public async Task TryExpireSetup_still_expires_a_setup_genuinely_missing_a_signal()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var setup = NewSetup(tenantId);
        await _repository.TryCreateAsync(setup, CancellationToken.None);

        var expired = await _repository.TryExpireSetupAsync(
            tenantId, setup.ItemId, DateTime.UtcNow, CancellationToken.None);

        expired.Should().BeTrue();
        var stored = await _repository.GetByIdAsync(tenantId, setup.ItemId, CancellationToken.None);
        stored!.PaymentStatus.Should().Be(PaymentStatuses.Expired);
    }

    [Fact]
    public async Task GetSetupsReadyForCompletionAsync_returns_only_setups_with_both_signals()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var missingSignal = NewSetup(tenantId);
        missingSignal.CreatedAtUtc = DateTime.UtcNow;
        await _repository.TryCreateAsync(missingSignal, CancellationToken.None);

        var bothSignals = NewSetup(tenantId);
        bothSignals.SetupAuthorizationConfirmedAtUtc = DateTime.UtcNow;
        bothSignals.SetupTokenConfirmedAtUtc = DateTime.UtcNow;
        await _repository.TryCreateAsync(bothSignals, CancellationToken.None);

        var notASetup = NewPayment(tenantId);
        notASetup.PaymentStatus = PaymentStatuses.Processing;
        await _repository.TryCreateAsync(notASetup, CancellationToken.None);

        var ready = await _repository.GetSetupsReadyForCompletionAsync(tenantId, 50, CancellationToken.None);

        ready.Select(p => p.ItemId).Should().BeEquivalentTo([bothSignals.ItemId]);
    }

    /// <summary>
    /// PR #393 review (Finding, round 5): a setup with both signals already on record must be
    /// found by the recovery sweep no matter how many older, genuinely-incomplete setups sit
    /// ahead of it in the same tenant -- reproducing exactly the scenario the old shared
    /// "oldest N Processing setups" query got wrong. That query, capped at <c>limit</c> and
    /// sorted oldest-first, would have returned only the backlog here and never reached the
    /// ready setup at all. <see cref="IPaymentRepository.GetSetupsReadyForCompletionAsync"/> is
    /// filtered on readiness instead of position in an oldest-first window, so it finds the ready
    /// setup regardless of how large -- or how much older -- the unrelated backlog is.
    /// </summary>
    [Fact]
    public async Task GetSetupsReadyForCompletionAsync_finds_a_ready_setup_behind_a_backlog_larger_than_the_batch_cap()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        const int limit = 5;

        // More than one batch's worth of older setups, all still genuinely missing a signal --
        // the backlog the old shared query would have let crowd the ready setup out entirely.
        for (var i = 0; i < limit + 3; i++)
        {
            var stuck = NewSetup(tenantId);
            stuck.CreatedAtUtc = DateTime.UtcNow.AddHours(-2).AddSeconds(-i);
            await _repository.TryCreateAsync(stuck, CancellationToken.None);
        }

        // Newer than every backlog record, so an oldest-first, capped query would place it last
        // -- outside the window -- while it is, in fact, the only one actually ready right now.
        var ready = NewSetup(tenantId);
        ready.CreatedAtUtc = DateTime.UtcNow;
        ready.SetupAuthorizationConfirmedAtUtc = DateTime.UtcNow;
        ready.SetupTokenConfirmedAtUtc = DateTime.UtcNow;
        await _repository.TryCreateAsync(ready, CancellationToken.None);

        var found = await _repository.GetSetupsReadyForCompletionAsync(tenantId, limit, CancellationToken.None);

        found.Select(p => p.ItemId).Should().BeEquivalentTo([ready.ItemId]);
    }

    [Fact]
    public async Task GetPendingSetupAgeSummaryAsync_aggregates_the_oldest_age_per_missing_signal()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var olderMissingAuthorization = NewSetup(tenantId);
        olderMissingAuthorization.CreatedAtUtc = DateTime.UtcNow.AddHours(-3);
        olderMissingAuthorization.SetupTokenConfirmedAtUtc = DateTime.UtcNow.AddHours(-3);
        await _repository.TryCreateAsync(olderMissingAuthorization, CancellationToken.None);

        var newerMissingAuthorization = NewSetup(tenantId);
        newerMissingAuthorization.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        newerMissingAuthorization.SetupTokenConfirmedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repository.TryCreateAsync(newerMissingAuthorization, CancellationToken.None);

        var missingBoth = NewSetup(tenantId);
        missingBoth.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await _repository.TryCreateAsync(missingBoth, CancellationToken.None);

        var ready = NewSetup(tenantId);
        ready.SetupAuthorizationConfirmedAtUtc = DateTime.UtcNow;
        ready.SetupTokenConfirmedAtUtc = DateTime.UtcNow;
        await _repository.TryCreateAsync(ready, CancellationToken.None);

        var summary = await _repository.GetPendingSetupAgeSummaryAsync(tenantId, CancellationToken.None);

        summary.Should().HaveCount(2);

        var authorization = summary.Single(s => s.MissingSignal == "authorization");
        authorization.Count.Should().Be(2);
        authorization.OldestCreatedAtUtc.Should().BeCloseTo(
            olderMissingAuthorization.CreatedAtUtc, TimeSpan.FromSeconds(1));

        var both = summary.Single(s => s.MissingSignal == "both");
        both.Count.Should().Be(1);
        both.OldestCreatedAtUtc.Should().BeCloseTo(missingBoth.CreatedAtUtc, TimeSpan.FromSeconds(1));
    }
}
