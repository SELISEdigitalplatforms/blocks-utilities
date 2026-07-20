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
}
