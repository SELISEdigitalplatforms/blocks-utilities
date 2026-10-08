using System.Text.Json;
using FluentAssertions;
using FluentValidation.TestHelper;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Models.StoredPayment;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Payment.DomainService.Validators;

namespace XUnitTest.Payment;

public sealed class RecurringPaymentContractTests
{
    [Theory]
    [InlineData("Subscription")]
    [InlineData("UnscheduledCardOnFile")]
    public void Validator_accepts_supported_recurring_models(
        string model)
    {
        var request = ValidRequest();
        request.RecurringProcessingModel = model;

        var result =
            new CreateRecurringPaymentRequestValidator()
                .TestValidate(request);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_rejects_card_on_file_for_merchant_initiated_charge()
    {
        var request = ValidRequest();
        request.RecurringProcessingModel = "CardOnFile";

        var result =
            new CreateRecurringPaymentRequestValidator()
                .TestValidate(request);

        result.ShouldHaveValidationErrorFor(
            value => value.RecurringProcessingModel);
    }

    [Fact]
    public void Request_factory_creates_continuing_authority_charge()
    {
        var payment = new PaymentDetail
        {
            TenantId = "tenant-1",
            OrganizationId = "organization-1",
            CurrencyCode = "EUR",
            ShopperReference = "shopper-reference",
            RecurringProcessingModel = "Subscription",
            Description = "Monthly membership"
        };
        var provider = new PaymentProvider
        {
            MerchantId = "merchant"
        };

        var request =
            new StoredPaymentChargeRequestFactory().Create(
                payment,
                provider,
                new StoredPaymentMethod
                {
                    ProviderPayerReference = "cus_123"
                },
                "provider-reference",
                "provider-token",
                1250);

        request.ShopperInteraction.Should().Be("ContAuth");
        request.RecurringProcessingModel
            .Should().Be("Subscription");
        request.PaymentMethod.StoredPaymentMethodId
            .Should().Be("provider-token");
        request.Amount.Value.Should().Be(1250);
        request.Amount.Currency.Should().Be("EUR");
        request.Reference.Should().Be("provider-reference");
    }

    [Fact]
    public void Public_request_never_accepts_provider_token_or_shopper_reference()
    {
        var properties = typeof(CreateRecurringPaymentRequest)
            .GetProperties()
            .Select(property => property.Name);

        properties.Should().NotContain(
            [
                "StoredPaymentMethodToken",
                "ShopperReference",
                "ProviderToken"
            ]);
    }

    [Fact]
    public void Request_factory_names_the_providers_store_so_adyen_finds_its_acquirer()
    {
        var request = new StoredPaymentChargeRequestFactory().Create(
            new PaymentDetail { CurrencyCode = "CHF", TenantId = "tenant-1" },
            new PaymentProvider { MerchantId = "merchant", StoreId = "store-1" },
            new StoredPaymentMethod(),
            "provider-reference",
            "provider-token",
            2999);

        request.Store.Should().Be("store-1");
        JsonDocument.Parse(JsonSerializer.Serialize(request))
            .RootElement.GetProperty("store").GetString()
            .Should().Be(
                "store-1",
                because: "without the store Adyen looks only at merchant-account level, finds no " +
                         "acquirer, and refuses every renewal and seat increase with 905_1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Request_factory_sends_no_store_when_the_provider_has_none(string? storeId)
    {
        var request = new StoredPaymentChargeRequestFactory().Create(
            new PaymentDetail { CurrencyCode = "CHF", TenantId = "tenant-1" },
            new PaymentProvider { MerchantId = "merchant", StoreId = storeId },
            new StoredPaymentMethod(),
            "provider-reference",
            "provider-token",
            2999);

        JsonDocument.Parse(JsonSerializer.Serialize(request))
            .RootElement.TryGetProperty("store", out _)
            .Should().BeFalse(
                because: "an account without stores must keep sending the body it always has; " +
                         "a blank store is an unknown store to Adyen");
    }

    [Fact]
    public async Task Provider_gateway_uses_payments_endpoint_and_idempotency()
    {
        var http = StubAdyenHttp.Json(new StoredPaymentChargeResponse
        {
            PspReference = "psp-reference",
            MerchantReference = "provider-reference",
            ResultCode = "Received",
            Amount = new ProviderAmount
            {
                Value = 1250,
                Currency = "EUR"
            }
        });

        var result = await StoredPaymentChargeProviderGatewayTests.Gateway(http)
            .ChargeAsync(
                Provider(),
                ChargeRequest(),
                "idempotency-key",
                CancellationToken.None);

        result.Outcome.Should().Be(
            StoredPaymentChargeOutcome.Accepted);
        result.PspReference.Should().Be("psp-reference");

        var sent = http.Requests.Should().ContainSingle().Subject;
        sent.Url.Should().Be("https://checkout-test.adyen.com/v72/payments");
        sent.Headers["x-api-key"].Should().Be("secret");
        sent.Headers["idempotency-key"].Should().Be("idempotency-key");
    }

    [Fact]
    public async Task Provider_gateway_rejects_mismatched_response()
    {
        var http = StubAdyenHttp.Json(new StoredPaymentChargeResponse
        {
            PspReference = "psp-reference",
            MerchantReference = "wrong-reference",
            Amount = new ProviderAmount
            {
                Value = 1250,
                Currency = "EUR"
            }
        });

        var result = await StoredPaymentChargeProviderGatewayTests.Gateway(http)
            .ChargeAsync(
                Provider(),
                ChargeRequest(),
                "idempotency-key",
                CancellationToken.None);

        result.Outcome.Should().Be(
            StoredPaymentChargeOutcome.OutcomeUnknown);
    }

    private static PaymentProvider Provider() => new()
    {
        ProviderName = PaymentConstants.AdyenOnlineProvider,
        ApiBaseUrl =
            "https://checkout-test.adyen.com/v72",
        ApiKey = "secret",
        MerchantId = "merchant"
    };

    private static StoredPaymentChargeRequest
        ChargeRequest() =>
        new()
        {
            MerchantAccount = "merchant",
            Amount = new ProviderAmount
            {
                Value = 1250,
                Currency = "EUR"
            },
            Reference = "provider-reference",
            PaymentMethod = new StoredPaymentChargeMethod
            {
                StoredPaymentMethodId = "provider-token"
            },
            ShopperReference = "shopper-reference",
            RecurringProcessingModel = "Subscription"
        };

    private static CreateRecurringPaymentRequest
        ValidRequest() =>
        new()
        {
            ProviderName =
                PaymentConstants.AdyenOnlineProvider,
            StoredPaymentMethodId = "method-1",
            Amount = 12.50m,
            CurrencyCode = "EUR",
            OrderId = "order-1",
            RecurringProcessingModel = "Subscription"
        };
}
