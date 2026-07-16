using System.Text.Json;
using FluentAssertions;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

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
}
