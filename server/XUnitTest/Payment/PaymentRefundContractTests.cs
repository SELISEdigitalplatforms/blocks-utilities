using FluentAssertions;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Validators;

namespace XUnitTest.Payment;

public sealed class PaymentRefundContractTests
{
    [Fact]
    public void Refund_request_requires_a_positive_amount()
    {
        var result =
            new CreatePaymentRefundRequestValidator()
                .Validate(new CreatePaymentRefundRequest
                {
                    Amount = 0
                });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Refund_request_limits_the_safe_reason_length()
    {
        var result =
            new CreatePaymentRefundRequestValidator()
                .Validate(new CreatePaymentRefundRequest
                {
                    Amount = 1,
                    Reason = new string('x', 281)
                });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Public_contract_does_not_accept_provider_references()
    {
        typeof(CreatePaymentRefundRequest)
            .GetProperties()
            .Select(property => property.Name)
            .Should()
            .BeEquivalentTo(
                nameof(CreatePaymentRefundRequest.Amount),
                nameof(CreatePaymentRefundRequest.Reason));
    }

    [Fact]
    public void Public_response_does_not_expose_provider_details()
    {
        typeof(PaymentRefundResponse)
            .GetProperties()
            .Select(property => property.Name)
            .Should()
            .NotContain(name =>
                name.Contains(
                    "Provider",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Psp",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Reason",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Refund_webhook_reference_round_trips_tenant_and_refund()
    {
        var service =
            new PaymentRefundWebhookReferenceService();
        var tenantId =
            "de9fc4f4baa4c4cbc829b6059b372dc6";
        var refundId = Guid.NewGuid().ToString();

        service.TryCreate(
                tenantId,
                refundId,
                out var reference)
            .Should()
            .BeTrue();
        service.TryParse(reference, out var route)
            .Should()
            .BeTrue();
        route.TenantId.Should().Be(tenantId);
        route.PaymentDetailId.Should().BeEmpty();
        route.RefundId.Should().Be(refundId);
    }

    [Fact]
    public void Refund_webhook_reference_rejects_tampering()
    {
        var service =
            new PaymentRefundWebhookReferenceService();
        service.TryCreate(
            "de9fc4f4baa4c4cbc829b6059b372dc6",
            Guid.NewGuid().ToString(),
            out var reference);

        service.TryParse(
                reference.Replace(
                    "r1.",
                    "r2.",
                    StringComparison.Ordinal),
                out _)
            .Should()
            .BeFalse();
    }
}
