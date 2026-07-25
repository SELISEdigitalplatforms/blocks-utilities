using System.Text;
using System.Text.Json;
using FluentAssertions;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class WebhookTenantResolverTests
{
    private const string TenantId = "de9fc4f4baa4c4cbc829b6059b372dc6";
    private const string ShopperKey = "shopper-reference-key-that-is-longer-than-thirty-two-bytes";

    private readonly ShopperReferenceService _shopperReferences = new();
    private readonly WebhookTenantResolver _resolver;

    public WebhookTenantResolverTests()
    {
        _resolver = new WebhookTenantResolver(
            new PaymentWebhookReferenceService(),
            new PaymentRefundWebhookReferenceService(),
            new PaymentCaptureWebhookReferenceService(),
            _shopperReferences);
    }

    [Fact]
    public void Standard_webhook_resolves_signed_reference_and_checks_legacy_metadata()
    {
        var references = new PaymentWebhookReferenceService();
        var paymentId = Guid.NewGuid().ToString();
        references.TryCreate(TenantId, paymentId, out var reference);
        var item = new NotificationItem
        {
            MerchantReference = reference
        };
        item.AdditionalData["metadata.value_a"] = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(TenantId));

        _resolver.TryResolveStandard(item, out var route)
            .Should().BeTrue();
        _resolver.IsMetadataConsistent(item, route.TenantId)
            .Should().BeTrue();
        route.PaymentDetailId.Should().Be(paymentId);
    }

    [Fact]
    public void Standard_webhook_rejects_conflicting_legacy_metadata()
    {
        var references = new PaymentWebhookReferenceService();
        references.TryCreate(TenantId, Guid.NewGuid().ToString(), out var reference);
        var item = new NotificationItem
        {
            MerchantReference = reference
        };
        item.AdditionalData["metadata.value_a"] = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("different-tenant"));

        _resolver.IsMetadataConsistent(item, TenantId)
            .Should().BeFalse();
    }

    [Fact]
    public void Token_webhook_resolves_tenant_from_shopper_reference()
    {
        _shopperReferences.TryCreate(
            TenantId,
            "actor-1",
            ShopperKey,
            out var shopperReference);
        using var document = JsonDocument.Parse(
            $$"""{"shopperReference":"{{shopperReference}}"}""");
        var request = new TokenWebhookRequest
        {
            Data = document.RootElement.Clone()
        };

        _resolver.TryResolveToken(request, out var tenantId)
            .Should().BeTrue();
        tenantId.Should().Be(TenantId);
    }

    [Fact]
    public void Token_webhook_rejects_non_object_payload()
    {
        using var document = JsonDocument.Parse("[]");
        var request = new TokenWebhookRequest { Data = document.RootElement.Clone() };

        _resolver.TryResolveToken(request, out var tenantId).Should().BeFalse();
        tenantId.Should().BeEmpty();
    }

    [Fact]
    public void Token_webhook_rejects_payload_without_shopper_reference()
    {
        using var document = JsonDocument.Parse("""{"other":"value"}""");
        var request = new TokenWebhookRequest { Data = document.RootElement.Clone() };

        _resolver.TryResolveToken(request, out _).Should().BeFalse();
    }

    [Fact]
    public void Metadata_without_tenant_key_is_treated_as_consistent()
    {
        var item = new NotificationItem();

        _resolver.IsMetadataConsistent(item, TenantId).Should().BeTrue();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("!!!not-base64!!!")]
    public void Metadata_with_unusable_encoded_tenant_is_inconsistent(string encoded)
    {
        var item = new NotificationItem();
        item.AdditionalData["metadata.value_a"] = encoded;

        _resolver.IsMetadataConsistent(item, TenantId).Should().BeFalse();
    }

    [Fact]
    public void Metadata_with_oversized_encoded_tenant_is_inconsistent()
    {
        var item = new NotificationItem();
        item.AdditionalData["metadata.value_a"] = new string('A', 129);

        _resolver.IsMetadataConsistent(item, TenantId).Should().BeFalse();
    }
}
