using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentResponseMapperTests
{
    private readonly PaymentResponseMapper _mapper = new();

    [Fact]
    public void Map_projects_precise_amount_and_instrument_when_present()
    {
        var expiry = DateTime.UtcNow.AddHours(1);
        var payment = new PaymentDetail
        {
            ItemId = "payment-1",
            ProviderName = "adyen",
            PaymentStatus = "Authorised",
            OrderId = "order-1",
            PreciseAmount = 12.34m,
            Amount = 99,
            CurrencyCode = "USD",
            RedirectUrl = "https://redirect",
            ExpirationDate = expiry,
            CheckoutSessionStatus = "completed",
            CheckoutResultCode = "authorised",
            PaymentFlow = "hosted",
            RecurringProcessingModel = "Subscription",
            CaptureStatus = "Captured",
            CaptureMode = "automatic",
            AuthorizedAmount = 12.34m,
            CapturedAmount = 12.34m,
            RefundedAmount = 1m,
            PaymentInstrument = new PaymentInstrument
            {
                Type = "scheme",
                Brand = "visa",
                LastFour = "4242",
                ExpiryMonth = "03",
                ExpiryYear = "2030",
                FundingSource = "credit",
                IssuerCountry = "US",
                IssuerName = "Bank"
            }
        };

        var response = _mapper.Map(payment);

        response.PaymentDetailId.Should().Be("payment-1");
        response.Amount.Should().Be(12.34m);
        response.ExpiresAtUtc.Should().Be(expiry);
        response.RecurringProcessingModel.Should().Be("Subscription");
        response.PaymentInstrument.Should().NotBeNull();
        response.PaymentInstrument!.Brand.Should().Be("visa");
        response.PaymentInstrument.LastFour.Should().Be("4242");
    }

    [Fact]
    public void Map_falls_back_to_amount_and_nulls_when_optional_fields_absent()
    {
        var payment = new PaymentDetail
        {
            ItemId = "payment-2",
            ProviderName = "adyen",
            PreciseAmount = 0m,
            Amount = 42,
            ExpirationDate = default,
            PaymentInstrument = null
        };

        var response = _mapper.Map(payment);

        response.Amount.Should().Be(42m);
        response.ExpiresAtUtc.Should().BeNull();
        response.PaymentInstrument.Should().BeNull();
    }
}
