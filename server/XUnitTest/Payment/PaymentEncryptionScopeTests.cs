using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// Which key ring protects a saved card's token — the fix for the case where a caller's
/// organization and the merchant account that issued the token are not the same thing.
/// </summary>
public sealed class PaymentEncryptionScopeTests
{
    [Fact]
    public void A_record_with_a_resolved_scope_uses_the_encryption_organization()
    {
        var method = new StoredPaymentMethod
        {
            TenantId = "tenant-1",
            OrganizationId = "caller-org",
            EncryptionOrganizationId = "merchant-org",
            EncryptionScopeResolvedAtUtc = DateTime.UtcNow
        };

        var scope = PaymentEncryptionScope.From(method);

        scope.OrganizationId.Should().Be("merchant-org",
            "the token is only usable at the merchant account that issued it, which need not " +
            "be the organization that happened to save the card");
    }

    [Fact]
    public void A_resolved_scope_can_be_the_tenant_level_ring_even_when_the_caller_has_an_organization()
    {
        var method = new StoredPaymentMethod
        {
            TenantId = "tenant-1",
            OrganizationId = "caller-org",
            EncryptionOrganizationId = null,
            EncryptionScopeResolvedAtUtc = DateTime.UtcNow
        };

        var scope = PaymentEncryptionScope.From(method);

        scope.OrganizationId.Should().BeNull(
            "an organization that is a subscriber of a tenant-level merchant account has no " +
            "ring of its own, and none should be inferred from its visibility scope");
    }

    [Fact]
    public void A_legacy_record_with_no_resolved_scope_falls_back_to_the_visibility_organization()
    {
        var method = new StoredPaymentMethod
        {
            TenantId = "tenant-1",
            OrganizationId = "caller-org",
            EncryptionOrganizationId = null,
            EncryptionScopeResolvedAtUtc = null
        };

        var scope = PaymentEncryptionScope.From(method);

        scope.OrganizationId.Should().Be("caller-org",
            "every organization was its own merchant when a record like this was written, so " +
            "the two fields held the same value at the time");
    }

    [Fact]
    public void The_encryption_organization_on_a_legacy_record_is_ignored()
    {
        // A record cannot actually be in this state today, but the derivation must not trust
        // EncryptionOrganizationId until EncryptionScopeResolvedAtUtc says it was actually set.
        var method = new StoredPaymentMethod
        {
            TenantId = "tenant-1",
            OrganizationId = "caller-org",
            EncryptionOrganizationId = "some-other-value",
            EncryptionScopeResolvedAtUtc = null
        };

        PaymentEncryptionScope.From(method).OrganizationId.Should().Be("caller-org");
    }
}
