using FluentAssertions;
using Moq;
using Payment.DomainService.Providers;
using Payment.DomainService.Requests;
using Payment.DomainService.Utilities;
using Payment.DomainService.Validators;

namespace XUnitTest.Payment;

public sealed class PaymentProviderCatalogTests
{
    private readonly PaymentProviderCatalog _catalog = new();

    [Theory]
    [InlineData("ADYEN-ONLINE")]
    [InlineData("adyen-online")]
    [InlineData("Adyen-Online")]
    public void Registered_provider_is_recognized_regardless_of_case(string providerName) =>
        _catalog.IsRegistered(providerName).Should().BeTrue();

    [Theory]
    [InlineData("OTHER")]
    [InlineData("PAYPAL")]
    [InlineData("adyen")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Unregistered_or_blank_provider_is_rejected(string? providerName) =>
        _catalog.IsRegistered(providerName).Should().BeFalse();

    [Theory]
    [InlineData("STRIPE")]
    [InlineData("stripe")]
    public void Stripe_is_registered_alongside_adyen(string providerName) =>
        _catalog.IsRegistered(providerName).Should().BeTrue();

    [Fact]
    public void Catalog_exposes_every_registered_provider() =>
        _catalog.RegisteredProviderNames
            .Should()
            .BeEquivalentTo(
                PaymentConstants.AdyenOnlineProvider,
                PaymentConstants.StripeProvider);

    [Fact]
    public void Validator_admits_whatever_the_catalog_registers()
    {
        var catalog = new Mock<IPaymentProviderCatalog>();
        catalog.Setup(x => x.IsRegistered("FUTURE-PROVIDER")).Returns(true);
        catalog.SetupGet(x => x.RegisteredProviderNames).Returns(["FUTURE-PROVIDER"]);

        var result = new MakePaymentRequestValidator(catalog.Object)
            .Validate(new MakePaymentRequest
            {
                ProviderName = "FUTURE-PROVIDER",
                Amount = 25.50m,
                CurrencyCode = "USD",
                OrderId = "order-1"
            });

        result.Errors
            .Select(error => error.PropertyName)
            .Should()
            .NotContain(nameof(MakePaymentRequest.ProviderName));
    }

    [Fact]
    public void Validator_reports_the_provider_error_code_for_unregistered_names()
    {
        var result = new MakePaymentRequestValidator(_catalog)
            .Validate(new MakePaymentRequest
            {
                ProviderName = "PAYPAL",
                Amount = 25.50m,
                CurrencyCode = "USD",
                OrderId = "order-1"
            });

        result.Errors
            .Should()
            .ContainSingle(error =>
                error.PropertyName == nameof(MakePaymentRequest.ProviderName))
            .Which.ErrorCode.Should().Be("payment_provider_not_supported");
    }
}
