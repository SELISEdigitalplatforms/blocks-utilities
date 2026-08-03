using FluentAssertions;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class CheckoutCallbackStateProtectorTests
{
    private const string ActiveKey = "return-state-key-that-is-longer-than-thirty-two-bytes";
    private const string PreviousKey = "previous-return-state-key-longer-than-thirty-two-bytes";

    [Fact]
    public void State_round_trips_and_contains_the_tenant_payment_provider_and_nonce()
    {
        var protector = new CheckoutCallbackStateProtector();
        var protectedState = protector.Create("tenant-a", null, "payment-1", "ADYEN-ONLINE", TimeSpan.FromMinutes(30), ActiveKey);

        protector.TryUnprotect(protectedState.Token, ActiveKey, null, out var state).Should().BeTrue();
        state.TenantId.Should().Be("tenant-a");
        state.PaymentDetailId.Should().Be("payment-1");
        state.ProviderName.Should().Be("ADYEN-ONLINE");
        state.Nonce.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The shopper's return must resolve the same configuration the payment used, and it has
    /// to do so in order to verify this very state — so the payment cannot be consulted first.
    /// The organization therefore travels signed, where it cannot be altered in the redirect.
    /// </summary>
    [Fact]
    public void State_carries_the_organization_that_started_the_payment()
    {
        var protector = new CheckoutCallbackStateProtector();
        var protectedState = protector.Create(
            "tenant-a", "organization-1", "payment-1", "ADYEN-ONLINE",
            TimeSpan.FromMinutes(30), ActiveKey);

        protector.TryUnprotect(protectedState.Token, ActiveKey, null, out var state)
            .Should().BeTrue();
        state.OrganizationId.Should().Be("organization-1");
    }

    /// <summary>
    /// A payment made through the tenant's own configuration names no organization, as does
    /// any state issued before organizations were scoped.
    /// </summary>
    [Fact]
    public void State_without_an_organization_still_reads()
    {
        var protector = new CheckoutCallbackStateProtector();
        var protectedState = protector.Create(
            "tenant-a", null, "payment-1", "ADYEN-ONLINE",
            TimeSpan.FromMinutes(30), ActiveKey);

        protector.TryUnprotect(protectedState.Token, ActiveKey, null, out var state)
            .Should().BeTrue();
        state.OrganizationId.Should().BeNull();
    }

    [Fact]
    public void State_accepts_previous_key_during_rotation_and_rejects_tampering()
    {
        var protector = new CheckoutCallbackStateProtector();
        var protectedState = protector.Create("tenant-a", null, "payment-1", "ADYEN-ONLINE", TimeSpan.FromMinutes(30), PreviousKey);

        protector.TryUnprotect(protectedState.Token, ActiveKey, PreviousKey, out _).Should().BeTrue();
        var tampered = protectedState.Token[..^1] + (protectedState.Token[^1] == 'A' ? "B" : "A");
        protector.TryUnprotect(tampered, ActiveKey, PreviousKey, out _).Should().BeFalse();
    }
}
