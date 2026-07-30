using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Providers.Adyen;
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
    public async Task Provider_gateway_uses_payments_endpoint_and_idempotency()
    {
        var response = new StoredPaymentChargeResponse
        {
            PspReference = "psp-reference",
            MerchantReference = "provider-reference",
            ResultCode = "Received",
            Amount = new ProviderAmount
            {
                Value = 1250,
                Currency = "EUR"
            }
        };
        var http = new Mock<IHttpService>(
            MockBehavior.Strict);
        using var source = new CancellationTokenSource();

        http.Setup(service =>
                service.SendRequest<
                    StoredPaymentChargeResponse>(
                    HttpMethod.Post,
                    "https://checkout-test.adyen.com/v72/payments",
                    It.IsAny<object>(),
                    "application/json",
                    It.Is<Dictionary<string, string>>(
                        headers =>
                            headers["x-api-key"] ==
                            "secret" &&
                            headers["idempotency-key"] ==
                            "idempotency-key"),
                    source.Token,
                    15))
            .ReturnsAsync((response, string.Empty));

        var result = await CreateGateway(http.Object)
            .ChargeAsync(
                Provider(),
                ChargeRequest(),
                "idempotency-key",
                source.Token);

        result.Outcome.Should().Be(
            StoredPaymentChargeOutcome.Accepted);
        result.PspReference.Should().Be("psp-reference");
        http.VerifyAll();
    }

    [Fact]
    public async Task Provider_gateway_rejects_mismatched_response()
    {
        var http = new Mock<IHttpService>();

        http.Setup(service =>
                service.SendRequest<
                    StoredPaymentChargeResponse>(
                    It.IsAny<HttpMethod>(),
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<int?>()))
            .ReturnsAsync((
                new StoredPaymentChargeResponse
                {
                    PspReference = "psp-reference",
                    MerchantReference = "wrong-reference",
                    Amount = new ProviderAmount
                    {
                        Value = 1250,
                        Currency = "EUR"
                    }
                },
                string.Empty));

        var result = await CreateGateway(http.Object)
            .ChargeAsync(
                Provider(),
                ChargeRequest(),
                "idempotency-key",
                CancellationToken.None);

        result.Outcome.Should().Be(
            StoredPaymentChargeOutcome.OutcomeUnknown);
    }

    private static CheckoutApiStoredPaymentChargeProviderGateway
        CreateGateway(IHttpService httpService)
    {
        var options =
            new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions
            {
                ProviderTimeoutSeconds = 15
            });

        return new CheckoutApiStoredPaymentChargeProviderGateway(
            httpService,
            new AdyenEndpointPolicy(),
            options.Object,
            NullLogger<
                CheckoutApiStoredPaymentChargeProviderGateway>
                .Instance);
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
