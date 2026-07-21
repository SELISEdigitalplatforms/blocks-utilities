using System.Text.Json;
using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class HostedCheckoutContractTests
{
    [Fact]
    public void Public_make_payment_contract_has_no_client_controlled_redirects_or_payment_mode()
    {
        var propertyNames = typeof(MakePaymentRequest).GetProperties().Select(x => x.Name).ToArray();

        propertyNames.Should().NotContain(["SuccessUrl", "FailUrl", "CancelUrl", "NotificationUrl", "PaymentMode"]);
    }

    [Fact]
    public void Hosted_session_request_serializes_consent_mode_and_not_legacy_store_flag()
    {
        var json = JsonSerializer.Serialize(new HostedCheckoutSessionRequest
        {
            MerchantAccount = "merchant",
            Amount = new ProviderAmount { Currency = "USD", Value = 100 },
            ReturnUrl = "https://payments.example/return?state=signed",
            Reference = "payment-1",
            Mode = "hosted",
            StorePaymentMethodMode = "askForConsent"
        });

        json.Should().Contain("\"mode\":\"hosted\"")
            .And.Contain("\"storePaymentMethodMode\":\"askForConsent\"")
            .And.NotContain("\"storePaymentMethod\"");
    }

    [Fact]
    public void Public_payment_response_does_not_expose_session_data_or_session_id()
    {
        var propertyNames = typeof(PaymentResponse).GetProperties().Select(x => x.Name).ToArray();

        propertyNames.Should().NotContain(["SessionData", "SessionId"]);
    }

    [Theory]
    [InlineData(true, "askForConsent")]
    [InlineData(false, "disabled")]
    public void Session_factory_configures_card_on_file_and_consent(
        bool savePaymentMethod,
        string expectedStoreMode)
    {
        var request = new MakePaymentRequest
        {
            SavePaymentMethod = savePaymentMethod,
            CustomerEmail = "shopper@example.com"
        };
        var payment = new PaymentDetail
        {
            TenantId = "tenant-1",
            CurrencyCode = "EUR"
        };
        var provider = new PaymentProvider
        {
            MerchantId = "merchant",
            CountryCode = "NL"
        };

        var result = new HostedCheckoutSessionRequestFactory()
            .Create(
                request,
                new PaymentExecutionContext(
                    "tenant-1",
                    "actor-1",
                    "organization-1"),
                payment,
                provider,
                "https://payments.example/return",
                "payment-reference",
                "shopper-reference",
                includeStoredPaymentMethods: true,
                minorUnits: 100);

        result.StorePaymentMethodMode.Should()
            .Be(expectedStoreMode);
        result.ShopperReference.Should()
            .Be("shopper-reference");
        result.ShopperInteraction.Should().Be("Ecommerce");
        result.RecurringProcessingModel.Should()
            .Be("CardOnFile");
    }

    [Fact]
    public void Session_factory_hides_stored_methods_during_removal()
    {
        var result = new HostedCheckoutSessionRequestFactory()
            .Create(
                new MakePaymentRequest
                {
                    SavePaymentMethod = false
                },
                new PaymentExecutionContext(
                    "tenant-1",
                    "actor-1",
                    null),
                new PaymentDetail
                {
                    TenantId = "tenant-1",
                    CurrencyCode = "EUR"
                },
                new PaymentProvider
                {
                    MerchantId = "merchant",
                    CountryCode = "NL"
                },
                "https://payments.example/return",
                "payment-reference",
                "shopper-reference",
                includeStoredPaymentMethods: false,
                minorUnits: 100);

        result.ShopperReference.Should().BeNull();
        result.RecurringProcessingModel.Should().BeNull();
        result.StorePaymentMethodMode.Should().Be("disabled");
    }
}
