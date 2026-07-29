using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class AdyenInitiationRequestFactoryTests
{
    private readonly AdyenInitiationRequestFactory _factory = new();

    [Fact]
    public void Supports_only_the_adyen_online_provider()
    {
        _factory.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeTrue();
        _factory.Supports("adyen-online").Should().BeTrue();
        _factory.Supports("STRIPE").Should().BeFalse();
    }

    [Fact]
    public void Envelope_promotes_the_fields_the_rest_of_the_system_reads()
    {
        var result = Create(Provider());

        result.ProviderName.Should().Be(PaymentConstants.AdyenOnlineProvider);
        result.Reference.Should().Be("payment-reference");
        result.MerchantAccount.Should().Be("merchant");
        result.AmountMinorUnits.Should().Be(2500);
        result.CurrencyCode.Should().Be("EUR");
        result.ReturnUrl.Should().Be("https://payments.example/return");
        result.SiteId.Should().Be("site-1");
    }

    [Fact]
    public void Payload_round_trips_back_to_the_adyen_request()
    {
        var session = AdyenInitiationRequestFactory.ReadSession(Create(Provider()));

        session.MerchantAccount.Should().Be("merchant");
        session.Reference.Should().Be("payment-reference");
        session.ReturnUrl.Should().Be("https://payments.example/return");
        session.Mode.Should().Be("hosted");
        session.Amount.Value.Should().Be(2500);
        session.Amount.Currency.Should().Be("EUR");
        session.CountryCode.Should().Be("NL");
        session.ShopperInteraction.Should().Be("Ecommerce");
        session.Metadata.SiteId.Should().Be("site-1");
    }

    [Theory]
    [InlineData(true, null, PaymentCaptureModes.Manual)]
    [InlineData(true, 5, PaymentCaptureModes.Manual)]
    [InlineData(false, 0, PaymentCaptureModes.AutomaticImmediate)]
    [InlineData(false, 5, PaymentCaptureModes.AutomaticDelayed)]
    [InlineData(false, null, PaymentCaptureModes.AccountDefault)]
    public void Capture_mode_is_resolved_from_the_provider_configuration(
        bool manualCapture,
        int? captureDelayHours,
        string expected)
    {
        var provider = Provider();
        provider.ManualCapture = manualCapture;
        provider.CaptureDelayHours = captureDelayHours;

        Create(provider).CaptureMode.Should().Be(expected);
    }

    [Fact]
    public void Manual_capture_suppresses_the_delay_sent_to_the_provider()
    {
        var provider = Provider();
        provider.ManualCapture = true;
        provider.CaptureDelayHours = 5;

        Create(provider).CaptureDelayHours.Should().BeNull();
    }

    private ProviderInitiationRequest Create(PaymentProvider provider) =>
        _factory.Create(
            new MakePaymentRequest { CustomerEmail = "shopper@example.com" },
            new PaymentExecutionContext("tenant-1", "actor-1", "organization-1"),
            new PaymentDetail { TenantId = "tenant-1", CurrencyCode = "EUR" },
            provider,
            "https://payments.example/return",
            "payment-reference",
            "shopper-reference",
            includeStoredPaymentMethods: true,
            minorUnits: 2500);

    private static PaymentProvider Provider() => new()
    {
        ProviderName = PaymentConstants.AdyenOnlineProvider,
        MerchantId = "merchant",
        CountryCode = "NL",
        SiteId = "site-1"
    };
}
