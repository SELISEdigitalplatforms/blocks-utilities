using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
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
            InitiationRequest = new HostedCheckoutSessionRequest
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

        validator.IsValid(payment, result).Should().BeTrue();

        result.Reference = payment.ItemId;
        validator.IsValid(payment, result).Should().BeFalse();
    }
}
