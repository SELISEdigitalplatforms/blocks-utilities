using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class CheckoutResultValidatorTests
{
    [Fact]
    public void Checkout_result_uses_the_provider_reference_snapshot()
    {
        var minorUnits = new Mock<ICurrencyMinorUnitResolver>();
        long expected = 1050;
        minorUnits.Setup(resolver => resolver.TryConvert(
                10.50m,
                "USD",
                out expected))
            .Returns(true);
        var payment = new PaymentDetail
        {
            ItemId = Guid.NewGuid().ToString(),
            SessionId = "session-1",
            PreciseAmount = 10.50m,
            CurrencyCode = "USD",
            InitiationRequest = new ProviderInitiationRequest
            {
                Reference = "p1.tenant-token.payment-reference"
            }
        };
        var result = new HostedCheckoutResult
        {
            Id = "session-1",
            Reference = payment.InitiationRequest.Reference,
            Amount = new ProviderAmount
            {
                Value = expected,
                Currency = "USD"
            }
        };
        var validator = new CheckoutResultValidator(minorUnits.Object);

        validator.Validate(payment, result)
            .Should().Be(CheckoutResultValidationOutcome.Valid);

        result.Reference = payment.ItemId;
        validator.Validate(payment, result)
            .Should().Be(CheckoutResultValidationOutcome.Mismatch);
    }

    [Fact]
    public void Missing_provider_amount_is_reported_as_unavailable_instead_of_mismatch()
    {
        var minorUnits = new Mock<ICurrencyMinorUnitResolver>();
        long expected = 1000;
        minorUnits.Setup(resolver => resolver.TryConvert(
                10m,
                "CHF",
                out expected))
            .Returns(true);
        var payment = new PaymentDetail
        {
            ItemId = "payment-1",
            SessionId = "session-1",
            PreciseAmount = 10m,
            CurrencyCode = "CHF",
            InitiationRequest = new ProviderInitiationRequest
            {
                Reference = "provider-reference"
            }
        };
        var result = new HostedCheckoutResult
        {
            Id = "session-1",
            Reference = "provider-reference",
            Status = "completed"
        };
        var validator = new CheckoutResultValidator(minorUnits.Object);

        validator.Validate(payment, result)
            .Should().Be(
                CheckoutResultValidationOutcome.ProviderDataUnavailable);
    }

    [Fact]
    public void Returned_provider_amount_that_differs_is_reported_as_mismatch()
    {
        var minorUnits = new Mock<ICurrencyMinorUnitResolver>();
        long expected = 1000;
        minorUnits.Setup(resolver => resolver.TryConvert(
                10m,
                "CHF",
                out expected))
            .Returns(true);
        var payment = new PaymentDetail
        {
            ItemId = "payment-1",
            SessionId = "session-1",
            PreciseAmount = 10m,
            CurrencyCode = "CHF"
        };
        var result = new HostedCheckoutResult
        {
            Id = "session-1",
            Reference = "payment-1",
            Status = "completed",
            Payments =
            [
                new HostedCheckoutPayment
                {
                    Amount = new ProviderAmount
                    {
                        Value = 999,
                        Currency = "CHF"
                    }
                }
            ]
        };
        var validator = new CheckoutResultValidator(minorUnits.Object);

        validator.Validate(payment, result)
            .Should().Be(CheckoutResultValidationOutcome.Mismatch);
    }
}
