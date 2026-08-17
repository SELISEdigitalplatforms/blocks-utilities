using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Integration;

[Collection(MongoIntegrationCollection.Name)]
public sealed class StoredPaymentMethodRepositoryIntegrationTests
{
    private readonly StoredPaymentMethodRepository _repository;

    public StoredPaymentMethodRepositoryIntegrationTests(MongoIntegrationFixture fixture) =>
        _repository = new StoredPaymentMethodRepository(fixture.DbContextProvider);

    private static StoredPaymentMethod NewMethod(string tenantId, string shopperReference) => new()
    {
        ItemId = Guid.NewGuid().ToString(),
        TenantId = tenantId,
        ShopperReference = shopperReference,
        ProviderName = "adyen",
        ProviderTokenCiphertext = "cipher",
        ProviderTokenFingerprint = Guid.NewGuid().ToString("N"),
        TokenEncryptionKeyId = "key-1",
        Status = PaymentMethodStatus.Active,
        Type = "scheme",
        Brand = "visa",
        LastFour = "4242"
    };

    /// <summary>
    /// The merchant account whose key ring protected the token, which is not the caller the card
    /// is offered to once one configuration serves several organizations.
    /// </summary>
    [Fact]
    public async Task Saving_a_card_records_the_scope_that_encrypted_its_token()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);
        method.OrganizationId = "org-subscriber";
        method.EncryptionOrganizationId = "default";
        method.EncryptionScopeResolvedAtUtc = DateTime.UtcNow;

        await _repository.UpsertFromProviderAsync(method, DateTime.UtcNow, CancellationToken.None);

        var stored = await _repository.GetByTokenFingerprintAsync(
            tenantId, shopper, "adyen", method.ProviderTokenFingerprint!, CancellationToken.None);

        stored!.OrganizationId.Should().Be("org-subscriber");
        stored.EncryptionOrganizationId.Should().Be("default");
        stored.EncryptionScopeResolvedAtUtc.Should().NotBeNull();

        // Without both, PaymentEncryptionScope.From falls back to OrganizationId and reads the
        // token under the subscriber's ring rather than the one that sealed it.
        PaymentEncryptionScope.From(stored).OrganizationId.Should().Be("default");
    }

    /// <summary>
    /// A card written before the scope was recorded keeps decrypting the way it was written.
    /// </summary>
    [Fact]
    public async Task A_card_saved_without_a_recorded_scope_still_reads_under_its_organization()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);
        method.OrganizationId = "org-legacy";

        await _repository.UpsertFromProviderAsync(method, DateTime.UtcNow, CancellationToken.None);

        var stored = await _repository.GetByTokenFingerprintAsync(
            tenantId, shopper, "adyen", method.ProviderTokenFingerprint!, CancellationToken.None);

        stored!.EncryptionScopeResolvedAtUtc.Should().BeNull();
        PaymentEncryptionScope.From(stored).OrganizationId.Should().Be("org-legacy");
    }

    [Fact]
    public async Task Upsert_then_list_and_get_reflect_active_method()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);

        await _repository.UpsertFromProviderAsync(method, DateTime.UtcNow, CancellationToken.None);

        var active = await _repository.ListActiveAsync(tenantId, new[] { new StoredPaymentMethodLookupScope(shopper, null) }, CancellationToken.None);
        active.Should().ContainSingle();
        var stored = active.Single();
        stored.Brand.Should().Be("visa");

        (await _repository.GetAsync(tenantId, stored.ItemId, CancellationToken.None))
            .Should().NotBeNull();
        (await _repository.GetByTokenFingerprintAsync(
            tenantId, shopper, "adyen", method.ProviderTokenFingerprint!, CancellationToken.None))
            .Should().NotBeNull();
    }

    [Fact]
    public async Task Upsert_with_removed_status_marks_existing_removed()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);
        await _repository.UpsertFromProviderAsync(method, DateTime.UtcNow.AddMinutes(-5), CancellationToken.None);

        var removal = NewMethod(tenantId, shopper);
        removal.ProviderTokenFingerprint = method.ProviderTokenFingerprint;
        removal.Status = PaymentMethodStatus.Removed;
        await _repository.UpsertFromProviderAsync(removal, DateTime.UtcNow, CancellationToken.None);

        var active = await _repository.ListActiveAsync(tenantId, new[] { new StoredPaymentMethodLookupScope(shopper, null) }, CancellationToken.None);
        active.Should().BeEmpty();
    }

    [Fact]
    public async Task HasUnresolvedRemoval_true_when_removal_pending()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);
        await _repository.UpsertFromProviderAsync(method, DateTime.UtcNow, CancellationToken.None);
        var stored = (await _repository.ListActiveAsync(tenantId, new[] { new StoredPaymentMethodLookupScope(shopper, null) }, CancellationToken.None)).Single();

        (await _repository.HasUnresolvedRemovalAsync(tenantId, shopper, CancellationToken.None))
            .Should().BeFalse();

        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimRemovalAsync(
            tenantId, stored.ItemId, shopper, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        (await _repository.HasUnresolvedRemovalAsync(tenantId, shopper, CancellationToken.None))
            .Should().BeTrue();
    }

    /// <summary>
    /// A removal that exhausted its retries is never going to resolve on its own, so it must
    /// not keep blocking new saves. Counting it left the shopper unable to save a card ever
    /// again over a failure that was ours, recoverable only by editing the database.
    /// </summary>
    [Fact]
    public async Task HasUnresolvedRemoval_false_once_the_removal_was_given_up_on()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);
        await _repository.UpsertFromProviderAsync(method, DateTime.UtcNow, CancellationToken.None);
        var stored = (await _repository.ListActiveAsync(
            tenantId, new[] { new StoredPaymentMethodLookupScope(shopper, null) }, CancellationToken.None)).Single();
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimRemovalAsync(
            tenantId, stored.ItemId, shopper, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        await _repository.MarkRemovalRequiresAttentionAsync(
            tenantId, stored.ItemId, leaseId, "provider_failure", CancellationToken.None);

        (await _repository.HasUnresolvedRemovalAsync(tenantId, shopper, CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Claim_for_payment_then_release_manages_lease()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);
        await _repository.UpsertFromProviderAsync(method, DateTime.UtcNow, CancellationToken.None);
        var stored = (await _repository.ListActiveAsync(tenantId, new[] { new StoredPaymentMethodLookupScope(shopper, null) }, CancellationToken.None)).Single();

        var leaseId = Guid.NewGuid().ToString();
        var claimed = await _repository.TryClaimForPaymentAsync(
            tenantId, stored.ItemId, shopper, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);
        claimed.Should().NotBeNull();
        claimed!.PaymentUseLeaseId.Should().Be(leaseId);

        await _repository.ReleasePaymentClaimAsync(tenantId, stored.ItemId, leaseId, CancellationToken.None);
        (await _repository.GetAsync(tenantId, stored.ItemId, CancellationToken.None))!
            .PaymentUseLeaseId.Should().BeNull();
    }

    [Fact]
    public async Task Removal_lifecycle_claim_then_mark_removed()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);
        await _repository.UpsertFromProviderAsync(method, DateTime.UtcNow, CancellationToken.None);
        var stored = (await _repository.ListActiveAsync(tenantId, new[] { new StoredPaymentMethodLookupScope(shopper, null) }, CancellationToken.None)).Single();

        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimRemovalAsync(
            tenantId, stored.ItemId, shopper, leaseId,
            DateTime.UtcNow.AddMinutes(-1), CancellationToken.None);

        var due = await _repository.GetDueRemovalCandidatesAsync(
            tenantId, DateTime.UtcNow, 50, CancellationToken.None);
        due.Should().Contain(m => m.ItemId == stored.ItemId);

        var removed = await _repository.MarkRemovedAsync(
            tenantId, stored.ItemId, leaseId, DateTime.UtcNow, CancellationToken.None);
        removed.Should().BeTrue();
        (await _repository.GetAsync(tenantId, stored.ItemId, CancellationToken.None))!
            .Status.Should().Be(PaymentMethodStatus.Removed);
    }

    [Fact]
    public async Task MarkRemovalOutcomeUnknown_and_RequiresAttention_update_status()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);
        await _repository.UpsertFromProviderAsync(method, DateTime.UtcNow, CancellationToken.None);
        var stored = (await _repository.ListActiveAsync(tenantId, new[] { new StoredPaymentMethodLookupScope(shopper, null) }, CancellationToken.None)).Single();
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimRemovalAsync(
            tenantId, stored.ItemId, shopper, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        (await _repository.MarkRemovalOutcomeUnknownAsync(
            tenantId, stored.ItemId, leaseId, DateTime.UtcNow.AddMinutes(-1), "err",
            CancellationToken.None)).Should().BeTrue();
        (await _repository.GetAsync(tenantId, stored.ItemId, CancellationToken.None))!
            .Status.Should().Be(PaymentMethodStatus.RemovalOutcomeUnknown);

        // re-claim the due removal to obtain a new lease
        var newLease = Guid.NewGuid().ToString();
        await _repository.TryClaimDueRemovalAsync(
            tenantId, stored.ItemId, newLease, DateTime.UtcNow.AddMinutes(5),
            DateTime.UtcNow, CancellationToken.None);

        (await _repository.MarkRemovalRequiresAttentionAsync(
            tenantId, stored.ItemId, newLease, "manual", CancellationToken.None))
            .Should().BeTrue();
        (await _repository.GetAsync(tenantId, stored.ItemId, CancellationToken.None))!
            .Status.Should().Be(PaymentMethodStatus.RemovalRequiresAttention);
    }

    [Fact]
    public async Task MarkRemovedFromProvider_removes_by_token_fingerprint()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);
        await _repository.UpsertFromProviderAsync(method, DateTime.UtcNow.AddMinutes(-5), CancellationToken.None);

        await _repository.MarkRemovedFromProviderAsync(
            tenantId, shopper, method.ProviderTokenFingerprint!, DateTime.UtcNow, CancellationToken.None);

        (await _repository.ListActiveAsync(tenantId, new[] { new StoredPaymentMethodLookupScope(shopper, null) }, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Reactivate_after_fresh_consent_restores_removed_method()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);
        var removedAt = DateTime.UtcNow.AddMinutes(-30);
        await _repository.UpsertFromProviderAsync(method, removedAt.AddMinutes(-5), CancellationToken.None);
        var stored = (await _repository.ListActiveAsync(tenantId, new[] { new StoredPaymentMethodLookupScope(shopper, null) }, CancellationToken.None)).Single();
        await _repository.MarkRemovedFromProviderAsync(
            tenantId, shopper, method.ProviderTokenFingerprint!, removedAt, CancellationToken.None);

        var refreshed = NewMethod(tenantId, shopper);
        refreshed.ItemId = stored.ItemId;
        refreshed.ProviderTokenFingerprint = method.ProviderTokenFingerprint;

        var reactivated = await _repository.ReactivateAfterFreshConsentAsync(
            refreshed, paymentCreatedAtUtc: DateTime.UtcNow, eventDateUtc: DateTime.UtcNow,
            CancellationToken.None);

        reactivated.Should().BeTrue();
        (await _repository.ListActiveAsync(tenantId, new[] { new StoredPaymentMethodLookupScope(shopper, null) }, CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task MigrateLegacyToken_populates_ciphertext_when_absent()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var legacy = NewMethod(tenantId, shopper);
        legacy.ProviderTokenCiphertext = null;
        await _repository.UpsertFromProviderAsync(legacy, DateTime.UtcNow, CancellationToken.None);
        var stored = (await _repository.ListActiveAsync(tenantId, new[] { new StoredPaymentMethodLookupScope(shopper, null) }, CancellationToken.None)).Single();
        stored.ProviderTokenCiphertext.Should().BeNull();

        await _repository.MigrateLegacyTokenAsync(
            tenantId, stored.ItemId,
            new ProtectedProviderToken("new-cipher", "new-fingerprint", "new-key"),
            CancellationToken.None);

        (await _repository.GetAsync(tenantId, stored.ItemId, CancellationToken.None))!
            .ProviderTokenCiphertext.Should().Be("new-cipher");
    }

    /// <summary>
    /// Two organizations must not see each other's cards even when their shopper references
    /// collide.
    /// </summary>
    /// <remarks>
    /// Registration deliberately accepts an existing shopper-reference key so a migration does
    /// not orphan saved cards. Supplying one key to two organizations makes them derive the
    /// same reference for the same person, and the reference alone would then match both. The
    /// card is only chargeable at the merchant account that issued it, so offering it at the
    /// other one declines a card the shopper can see is fine.
    /// </remarks>
    [Fact]
    public async Task A_shared_shopper_reference_does_not_leak_cards_between_organizations()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();

        var first = NewMethod(tenantId, shopper);
        first.OrganizationId = "organization-1";
        var second = NewMethod(tenantId, shopper);
        second.OrganizationId = "organization-2";

        await _repository.UpsertFromProviderAsync(first, DateTime.UtcNow, CancellationToken.None);
        await _repository.UpsertFromProviderAsync(second, DateTime.UtcNow, CancellationToken.None);

        var forFirst = await _repository.ListActiveAsync(
            tenantId,
            new[] { new StoredPaymentMethodLookupScope(shopper, "organization-1") },
            CancellationToken.None);

        forFirst.Should().ContainSingle();
        forFirst.Single().ItemId.Should().Be(first.ItemId);
    }

    /// <summary>
    /// An organization with no configuration of its own resolves the tenant's, so the cards it
    /// may be offered are the tenant-level ones — that same configuration can charge them.
    /// Every card saved before organizations existed lives in this scope.
    /// </summary>
    [Fact]
    public async Task The_tenant_level_scope_lists_only_cards_with_no_organization()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();

        var legacy = NewMethod(tenantId, shopper);
        var scoped = NewMethod(tenantId, shopper);
        scoped.OrganizationId = "organization-1";

        await _repository.UpsertFromProviderAsync(legacy, DateTime.UtcNow, CancellationToken.None);
        await _repository.UpsertFromProviderAsync(scoped, DateTime.UtcNow, CancellationToken.None);

        var tenantLevel = await _repository.ListActiveAsync(
            tenantId,
            new[] { new StoredPaymentMethodLookupScope(shopper, null) },
            CancellationToken.None);

        tenantLevel.Should().ContainSingle();
        tenantLevel.Single().ItemId.Should().Be(legacy.ItemId);
    }

    /// <summary>
    /// Scopes are matched as pairs. Matching references and organizations as two independent
    /// sets would admit any combination of them, which is a wider leak than the one closed.
    /// </summary>
    [Fact]
    public async Task Scopes_match_as_pairs_not_as_two_independent_sets()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var firstShopper = Guid.NewGuid().ToString();
        var secondShopper = Guid.NewGuid().ToString();

        var wanted = NewMethod(tenantId, firstShopper);
        wanted.OrganizationId = "organization-1";
        // Matches the other scope's reference and this scope's organization, so a cross product
        // would return it.
        var crossed = NewMethod(tenantId, secondShopper);
        crossed.OrganizationId = "organization-1";

        await _repository.UpsertFromProviderAsync(wanted, DateTime.UtcNow, CancellationToken.None);
        await _repository.UpsertFromProviderAsync(crossed, DateTime.UtcNow, CancellationToken.None);

        var listed = await _repository.ListActiveAsync(
            tenantId,
            new[]
            {
                new StoredPaymentMethodLookupScope(firstShopper, "organization-1"),
                new StoredPaymentMethodLookupScope(secondShopper, "organization-2")
            },
            CancellationToken.None);

        listed.Should().ContainSingle();
        listed.Single().ItemId.Should().Be(wanted.ItemId);
    }
}
