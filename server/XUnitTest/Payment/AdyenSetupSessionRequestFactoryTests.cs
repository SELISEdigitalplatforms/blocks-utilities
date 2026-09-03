using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Requests;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class AdyenSetupSessionRequestFactoryTests
{
    private readonly AdyenSetupSessionRequestFactory _factory = new();

    [Fact]
    public void Supports_only_the_adyen_online_provider()
    {
        _factory.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeTrue();
        _factory.Supports("adyen-online").Should().BeTrue();
        _factory.Supports("STRIPE").Should().BeFalse();
    }

    [Fact]
    public void The_envelope_carries_zero_and_nothing_else_to_authorise()
    {
        var result = Create(Provider());

        result.ProviderName.Should().Be(PaymentConstants.AdyenOnlineProvider);
        result.Reference.Should().Be("setup-reference");
        result.MerchantAccount.Should().Be("merchant");
        result.AmountMinorUnits.Should().Be(0);
        result.CurrencyCode.Should().Be("EUR");
        result.ReturnUrl.Should().Be("https://payments.example/return");
        result.SiteId.Should().Be("site-1");
    }

    [Fact]
    public void The_session_requests_a_reusable_token_unconditionally()
    {
        var session = AdyenInitiationRequestFactory.ReadSession(Create(Provider()));

        // A card-on-file setup's entire purpose is a reusable token, so this must never depend on
        // any flag the paid checkout path conditions the same fields on.
        session.Amount.Value.Should().Be(0);
        session.Amount.Currency.Should().Be("EUR");
        session.StorePaymentMethodMode.Should().Be("askForConsent");
        // A setup token is for a subscription renewal -- a merchant-initiated, scheduled charge --
        // never a shopper-initiated one, so this must be "Subscription" and never the "CardOnFile"
        // this factory used to hardcode.
        session.RecurringProcessingModel.Should().Be(PaymentConstants.SubscriptionRecurringModel);
        session.RecurringProcessingModel.Should().Be("Subscription");
        session.ShopperReference.Should().Be("shopper-reference");
        session.ShopperInteraction.Should().Be("Ecommerce");
        session.Mode.Should().Be("hosted");
    }

    [Fact]
    public void The_session_restricts_allowed_payment_methods_to_cards_only()
    {
        var session = AdyenInitiationRequestFactory.ReadSession(Create(Provider()));

        // Zero-value authorisation is not documented as supported by every payment method Adyen
        // might otherwise offer -- cards are the one type reliably documented to accept
        // amount.value: 0, so a setup session must never advertise anything broader.
        session.AllowedPaymentMethods.Should().BeEquivalentTo(["scheme"]);
    }

    [Fact]
    public void The_country_code_comes_from_the_provider_configuration()
    {
        var session = AdyenInitiationRequestFactory.ReadSession(Create(Provider()));

        session.CountryCode.Should().Be("NL");
    }

    [Fact]
    public void The_shopper_email_is_carried_from_the_setup_request()
    {
        var session = AdyenInitiationRequestFactory.ReadSession(Create(
            Provider(),
            new CreatePaymentMethodSetupRequest
            {
                CurrencyCode = "EUR",
                CustomerEmail = "shopper@example.com"
            }));

        session.ShopperEmail.Should().Be("shopper@example.com");
    }

    private ProviderInitiationRequest Create(PaymentProvider provider) =>
        Create(
            provider,
            new CreatePaymentMethodSetupRequest
            {
                CurrencyCode = "EUR",
                CustomerEmail = "shopper@example.com"
            });

    private ProviderInitiationRequest Create(
        PaymentProvider provider,
        CreatePaymentMethodSetupRequest request) =>
        _factory.Create(
            request,
            new PaymentDetail { TenantId = "tenant-1", CurrencyCode = "EUR" },
            provider,
            "https://payments.example/return",
            "setup-reference",
            "shopper-reference",
            null);

    private static PaymentProvider Provider() => new()
    {
        ProviderName = PaymentConstants.AdyenOnlineProvider,
        MerchantId = "merchant",
        CountryCode = "NL",
        SiteId = "site-1"
    };
}
