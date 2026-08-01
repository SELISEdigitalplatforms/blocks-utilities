using FluentAssertions;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class WebhookTenantResolverTests
{
    private const string TenantId = "de9fc4f4baa4c4cbc829b6059b372dc6";
    private const string ShopperKey = "shopper-reference-key-that-is-longer-than-thirty-two-bytes";

    private readonly PaymentWebhookReferenceService _payments = new();
    private readonly PaymentRefundWebhookReferenceService _refunds = new();
    private readonly PaymentCaptureWebhookReferenceService _captures = new();
    private readonly ShopperReferenceService _shopperReferences = new();
    private readonly WebhookTenantResolver _resolver;

    public WebhookTenantResolverTests()
    {
        _resolver = new WebhookTenantResolver(
            _payments,
            _refunds,
            _captures,
            _shopperReferences);
    }

    [Fact]
    public void Payment_reference_resolves_to_its_tenant_and_payment()
    {
        var paymentId = Guid.NewGuid().ToString();
        _payments.TryCreate(TenantId, paymentId, out var reference);

        _resolver.TryResolvePayment(reference, out var route).Should().BeTrue();

        route.TenantId.Should().Be(TenantId);
        route.PaymentDetailId.Should().Be(paymentId);
        route.RefundId.Should().BeNull();
        route.CaptureId.Should().BeNull();
    }

    [Fact]
    public void Refund_reference_resolves_to_its_refund()
    {
        var refundId = Guid.NewGuid().ToString();
        _refunds.TryCreate(TenantId, refundId, out var reference);

        _resolver.TryResolvePayment(reference, out var route).Should().BeTrue();

        route.TenantId.Should().Be(TenantId);
        route.RefundId.Should().Be(refundId);
    }

    [Fact]
    public void Capture_reference_resolves_to_its_capture()
    {
        var captureId = Guid.NewGuid().ToString();
        _captures.TryCreate(TenantId, captureId, out var reference);

        _resolver.TryResolvePayment(reference, out var route).Should().BeTrue();

        route.TenantId.Should().Be(TenantId);
        route.CaptureId.Should().Be(captureId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-reference")]
    [InlineData(null)]
    public void Unrecognised_reference_does_not_resolve(string? reference) =>
        _resolver.TryResolvePayment(reference, out _).Should().BeFalse();

    [Fact]
    public void Shopper_reference_resolves_to_its_tenant()
    {
        _shopperReferences.TryCreate(TenantId, "actor-1", ShopperKey, out var shopperReference);

        _resolver.TryResolveTenant(shopperReference, out var tenantId).Should().BeTrue();
        tenantId.Should().Be(TenantId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("s1.not-a-token.deadbeef")]
    [InlineData(null)]
    public void Unrecognised_shopper_reference_does_not_resolve(string? shopperReference) =>
        _resolver.TryResolveTenant(shopperReference, out _).Should().BeFalse();
}
