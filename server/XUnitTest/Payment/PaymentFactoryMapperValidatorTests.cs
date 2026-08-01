using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentFactoryMapperValidatorTests
{
    [Fact]
    public void Refund_request_factory_maps_amount_currency_and_reference()
    {
        var refund = new PaymentRefund
        {
            ProviderMerchantAccount = "merchant-1",
            CurrencyCode = "EUR",
            ProviderReference = "refund-ref-1"
        };

        var request = new PaymentRefundRequestFactory().Create(refund, 2500);

        request.MerchantAccount.Should().Be("merchant-1");
        request.Amount.Value.Should().Be(2500);
        request.Amount.Currency.Should().Be("EUR");
        request.Reference.Should().Be("refund-ref-1");
    }

    [Fact]
    public void Refund_request_factory_creates_reversal_without_amount()
    {
        var refund = new PaymentRefund
        {
            ProviderMerchantAccount = "merchant-1",
            ProviderReference = "refund-ref-1"
        };

        var reversal = new PaymentRefundRequestFactory().CreateReversal(refund);

        reversal.MerchantAccount.Should().Be("merchant-1");
        reversal.Reference.Should().Be("refund-ref-1");
    }

    [Fact]
    public void Refund_response_mapper_projects_entity_onto_response()
    {
        var completedAt = DateTime.UtcNow;
        var refund = new PaymentRefund
        {
            RefundId = "refund-9",
            Status = "Completed",
            ProviderOperation = "Refund",
            CompletionAction = "None",
            Amount = 12.5m,
            CurrencyCode = "USD",
            FailureCode = null,
            FailureSummary = null,
            CompletedAtUtc = completedAt
        };

        var response = new PaymentRefundResponseMapper().Map("payment-1", refund);

        response.RefundId.Should().Be("refund-9");
        response.PaymentDetailId.Should().Be("payment-1");
        response.Status.Should().Be("Completed");
        response.Amount.Should().Be(12.5m);
        response.CurrencyCode.Should().Be("USD");
        response.CompletedAtUtc.Should().Be(completedAt);
    }

    [Fact]
    public void Capture_request_factory_maps_amount_currency_and_reference()
    {
        var capture = new PaymentCapture
        {
            ProviderMerchantAccount = "merchant-2",
            CurrencyCode = "GBP",
            ProviderReference = "capture-ref-1"
        };

        var request = new PaymentCaptureRequestFactory().Create(capture, 999);

        request.MerchantAccount.Should().Be("merchant-2");
        request.Amount.Value.Should().Be(999);
        request.Amount.Currency.Should().Be("GBP");
        request.Reference.Should().Be("capture-ref-1");
    }

    [Fact]
    public void Capture_response_mapper_projects_entity_onto_response()
    {
        var capture = new PaymentCapture
        {
            CaptureId = "capture-9",
            Status = "Completed",
            Amount = 7.25m,
            CurrencyCode = "CHF",
            FailureCode = "none"
        };

        var response = new PaymentCaptureResponseMapper().Map("payment-2", capture);

        response.CaptureId.Should().Be("capture-9");
        response.PaymentDetailId.Should().Be("payment-2");
        response.Status.Should().Be("Completed");
        response.Amount.Should().Be(7.25m);
        response.CurrencyCode.Should().Be("CHF");
    }

    [Theory]
    [InlineData("state", "session", "result", true)]
    [InlineData("", "session", "result", false)]
    [InlineData("state", "", "result", false)]
    // A session result is Adyen-specific; Stripe's return carries none, so the shared
    // validator accepts its absence and the Adyen result client rejects it instead.
    [InlineData("state", "session", "", true)]
    [InlineData("state", "session", null, true)]
    public void Callback_request_validator_enforces_required_fields(
        string state, string sessionId, string sessionResult, bool expected)
    {
        var validator = Validator(new PaymentOptions());
        var request = new CheckoutCallbackRequest(state, sessionId, sessionResult);

        validator.IsValid(request).Should().Be(expected);
    }

    [Fact]
    public void Callback_request_validator_rejects_oversized_state()
    {
        var validator = Validator(new PaymentOptions { MaximumReturnParameterLength = 512 });
        var request = new CheckoutCallbackRequest(
            new string('a', 513), "session", "result");

        validator.IsValid(request).Should().BeFalse();
    }

    [Fact]
    public void Callback_request_validator_rejects_oversized_session_id()
    {
        var validator = Validator(new PaymentOptions());
        var request = new CheckoutCallbackRequest(
            "state", new string('s', 257), "result");

        validator.IsValid(request).Should().BeFalse();
    }

    private static CheckoutCallbackRequestValidator Validator(PaymentOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(options);
        return new CheckoutCallbackRequestValidator(monitor.Object);
    }
}
