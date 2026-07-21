using FluentAssertions;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentCaptureWebhookReferenceServiceTests
{
    [Fact]
    public void Reference_round_trip_preserves_tenant_and_capture()
    {
        const string tenantId =
            "de9fc4f4baa4c4cbc829b6059b372dc6";
        var captureId = Guid.NewGuid().ToString();
        var service = new PaymentCaptureWebhookReferenceService();

        service.TryCreate(tenantId, captureId, out var reference)
            .Should().BeTrue();
        service.TryParse(reference, out var route)
            .Should().BeTrue();
        route.TenantId.Should().Be(tenantId);
        route.CaptureId.Should().Be(captureId);
        route.PaymentDetailId.Should().BeEmpty();
    }
}
