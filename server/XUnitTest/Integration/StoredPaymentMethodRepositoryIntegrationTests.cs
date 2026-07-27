using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;

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

    [Fact]
    public async Task Upsert_then_list_and_get_reflect_active_method()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);

        await _repository.UpsertFromProviderAsync(method, DateTime.UtcNow, CancellationToken.None);

        var active = await _repository.ListActiveAsync(tenantId, shopper, CancellationToken.None);
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

        var active = await _repository.ListActiveAsync(tenantId, shopper, CancellationToken.None);
        active.Should().BeEmpty();
    }

    [Fact]
    public async Task HasUnresolvedRemoval_true_when_removal_pending()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);
        await _repository.UpsertFromProviderAsync(method, DateTime.UtcNow, CancellationToken.None);
        var stored = (await _repository.ListActiveAsync(tenantId, shopper, CancellationToken.None)).Single();

        (await _repository.HasUnresolvedRemovalAsync(tenantId, shopper, CancellationToken.None))
            .Should().BeFalse();

        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimRemovalAsync(
            tenantId, stored.ItemId, shopper, leaseId,
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        (await _repository.HasUnresolvedRemovalAsync(tenantId, shopper, CancellationToken.None))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Claim_for_payment_then_release_manages_lease()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);
        await _repository.UpsertFromProviderAsync(method, DateTime.UtcNow, CancellationToken.None);
        var stored = (await _repository.ListActiveAsync(tenantId, shopper, CancellationToken.None)).Single();

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
        var stored = (await _repository.ListActiveAsync(tenantId, shopper, CancellationToken.None)).Single();

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
        var stored = (await _repository.ListActiveAsync(tenantId, shopper, CancellationToken.None)).Single();
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

        (await _repository.ListActiveAsync(tenantId, shopper, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Reactivate_after_fresh_consent_restores_removed_method()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var method = NewMethod(tenantId, shopper);
        var removedAt = DateTime.UtcNow.AddMinutes(-30);
        await _repository.UpsertFromProviderAsync(method, removedAt.AddMinutes(-5), CancellationToken.None);
        var stored = (await _repository.ListActiveAsync(tenantId, shopper, CancellationToken.None)).Single();
        await _repository.MarkRemovedFromProviderAsync(
            tenantId, shopper, method.ProviderTokenFingerprint!, removedAt, CancellationToken.None);

        var refreshed = NewMethod(tenantId, shopper);
        refreshed.ItemId = stored.ItemId;
        refreshed.ProviderTokenFingerprint = method.ProviderTokenFingerprint;

        var reactivated = await _repository.ReactivateAfterFreshConsentAsync(
            refreshed, paymentCreatedAtUtc: DateTime.UtcNow, eventDateUtc: DateTime.UtcNow,
            CancellationToken.None);

        reactivated.Should().BeTrue();
        (await _repository.ListActiveAsync(tenantId, shopper, CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task MigrateLegacyToken_populates_ciphertext_when_absent()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var shopper = Guid.NewGuid().ToString();
        var legacy = NewMethod(tenantId, shopper);
        legacy.ProviderTokenCiphertext = null;
        await _repository.UpsertFromProviderAsync(legacy, DateTime.UtcNow, CancellationToken.None);
        var stored = (await _repository.ListActiveAsync(tenantId, shopper, CancellationToken.None)).Single();
        stored.ProviderTokenCiphertext.Should().BeNull();

        await _repository.MigrateLegacyTokenAsync(
            tenantId, stored.ItemId,
            new ProtectedProviderToken("new-cipher", "new-fingerprint", "new-key"),
            CancellationToken.None);

        (await _repository.GetAsync(tenantId, stored.ItemId, CancellationToken.None))!
            .ProviderTokenCiphertext.Should().Be("new-cipher");
    }
}
