using FluentAssertions;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentWebhookReferenceServiceTests
{
    [Theory]
    [InlineData("de9fc4f4baa4c4cbc829b6059b372dc6")]
    [InlineData("de9fc4f4-baa4-c4cb-c829-b6059b372dc6")]
    [InlineData("Dde9fc4f4baa4c4cbc829b6059b372dc6")]
    public void Reference_round_trips_supported_tenant_formats(
        string tenantId)
    {
        var service = new PaymentWebhookReferenceService();
        var paymentId = Guid.NewGuid().ToString();

        var created = service.TryCreate(
            tenantId,
            paymentId,
            out var reference);
        var parsed = service.TryParse(
            reference,
            out var route);

        created.Should().BeTrue();
        parsed.Should().BeTrue();
        reference.Length.Should().BeLessThanOrEqualTo(80);
        route.TenantId.Should().Be(tenantId);
        route.PaymentDetailId.Should().Be(paymentId);
    }

    [Fact]
    public void Changed_reference_is_rejected()
    {
        var service = new PaymentWebhookReferenceService();

        service.TryParse(
                $"p1.invalid.{Guid.NewGuid()}",
                out _)
            .Should().BeFalse();
    }
}
