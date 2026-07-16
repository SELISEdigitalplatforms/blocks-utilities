using FluentAssertions;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class ShopperReferenceServiceTests
{
    private const string TenantId = "de9fc4f4baa4c4cbc829b6059b372dc6";
    private const string Key = "shopper-reference-key-that-is-longer-than-thirty-two-bytes";

    [Fact]
    public void Shopper_reference_is_deterministic_and_resolves_the_tenant()
    {
        var service = new ShopperReferenceService();

        service.TryCreate(TenantId, "actor-1", Key, out var first)
            .Should().BeTrue();
        service.TryCreate(TenantId, "actor-1", Key, out var second)
            .Should().BeTrue();
        service.TryResolveTenant(first, out var resolvedTenant)
            .Should().BeTrue();

        first.Should().Be(second);
        first.Should().NotContain("actor-1");
        resolvedTenant.Should().Be(TenantId);
    }

    [Fact]
    public void Malformed_shopper_reference_does_not_resolve_a_tenant()
    {
        var service = new ShopperReferenceService();

        service.TryResolveTenant(
                "s1.invalid.not-a-hash",
                out _)
            .Should().BeFalse();
    }
}
