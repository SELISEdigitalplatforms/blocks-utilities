using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Models.StoredPayment;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <remarks>
/// Guards the off-session Adyen charge behind renewals and seat increases: a payer told their card
/// declined when the merchant account refused it, and a refusal that can never succeed sent four
/// times before anyone was answered.
/// </remarks>
public sealed class StoredPaymentChargeProviderGatewayTests
{
    private const string ConfigurationRefusal =
        "{\"status\":500,\"errorCode\":\"905_1\",\"message\":\"Could not find an acquirer " +
        "account for the provided txvariant (visa), currency (CHF), and action (AUTH).\"," +
        "\"errorType\":\"configuration\",\"pspReference\":\"M99ZSJ9SSZTKRK75\"}";

    [Fact]
    public void Supports_matches_adyen_online_provider()
    {
        var gateway = Gateway(StubAdyenHttp.Returning(HttpStatusCode.OK, "{}"));
        gateway.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeTrue();
        gateway.Supports("stripe").Should().BeFalse();
    }

    [Fact]
    public async Task Charge_rejects_unsafe_endpoint_with_invalid_endpoint_code()
    {
        var http = StubAdyenHttp.Returning(HttpStatusCode.OK, "{}");
        var provider = Provider();
        provider.ApiBaseUrl = "https://10.0.0.5/v72";

        var result = await Gateway(http).ChargeAsync(
            provider, Request(), Guid.NewGuid().ToString(), CancellationToken.None);

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Unavailable);
        result.SafeErrorCode.Should().Be("provider_endpoint_invalid");
        http.Requests.Should().BeEmpty(
            because: "a card token must never be sent to a host that is not Adyen's");
    }

    [Fact]
    public async Task Charge_maps_matching_response_to_accepted()
    {
        var request = Request();
        var idempotencyKey = Guid.NewGuid().ToString();
        var http = StubAdyenHttp.Json(new StoredPaymentChargeResponse
        {
            PspReference = "charge-psp",
            MerchantReference = request.Reference,
            ResultCode = "Authorised",
            Amount = new ProviderAmount { Value = 1000, Currency = "EUR" }
        });

        var result = await Gateway(http).ChargeAsync(
            Provider(), request, idempotencyKey, CancellationToken.None);

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Accepted);
        result.PspReference.Should().Be("charge-psp");

        var sent = http.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Url.Should().Be("https://checkout-test.adyen.com/v72/payments");
        sent.Headers["x-api-key"].Should().Be("secret");
        sent.Headers["idempotency-key"].Should().Be(
            idempotencyKey,
            because: "the recovery sweep re-asks under the same key, and Adyen must see one charge");
        sent.Body.Should().Be(
            JsonSerializer.Serialize(request),
            because: "the body must be exactly what the platform HTTP service used to send");
    }

    [Fact]
    public async Task Charge_maps_error_code_response_to_rejected()
    {
        var result = await Charge(StubAdyenHttp.Json(
            new StoredPaymentChargeResponse { ErrorCode = "declined-42" }));

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Rejected);
        result.SafeErrorCode.Should().Be("declined-42");
        result.MerchantConfiguration.Should().BeFalse(
            because: "an ordinary refusal is the card's, and is reported as a decline");
    }

    [Fact]
    public async Task Charge_maps_client_error_status_to_rejected()
    {
        var result = await Charge(StubAdyenHttp.Json(
            new StoredPaymentChargeResponse { Status = 422 }));

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Rejected);
    }

    [Fact]
    public async Task Charge_maps_validation_package_error_to_rejected()
    {
        var result = await Charge(StubAdyenHttp.Returning(
            HttpStatusCode.BadRequest,
            "{\"status\":400,\"errorType\":\"validation\",\"errorCode\":\"amount_invalid\"}"));

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Rejected);
        result.SafeErrorCode.Should().Be("amount_invalid");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task A_merchant_configuration_refusal_is_rejected_and_flagged_as_not_the_cards(
        HttpStatusCode status)
    {
        // The exact answer Adyen gave in production: HTTP 500, but a setup problem, not an outage.
        var result = await Charge(StubAdyenHttp.Returning(
            status,
            ConfigurationRefusal.Replace("500", ((int)status).ToString(), StringComparison.Ordinal)));

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Rejected);
        result.SafeErrorCode.Should().Be("905_1");
        result.MerchantConfiguration.Should().BeTrue(
            because: "a payer told their card declined will try another card, which fails the same way");
    }

    [Fact]
    public async Task A_merchant_configuration_refusal_is_sent_once_not_retried()
    {
        var http = StubAdyenHttp.Returning(HttpStatusCode.InternalServerError, ConfigurationRefusal);

        await Charge(http);

        http.Requests.Should().ContainSingle(
            because: "Adyen answers 905_1 with HTTP 500, and every retry repeated the same refusal " +
                     "while the payer waited six to eleven seconds for it");
    }

    [Fact]
    public async Task Charge_still_treats_an_unclassified_server_error_as_unknown()
    {
        var http = StubAdyenHttp.Returning(
            HttpStatusCode.InternalServerError,
            "{\"status\":500,\"errorCode\":\"905\",\"errorType\":\"internal\"}");

        var result = await Charge(http);

        result.Outcome.Should().Be(
            StoredPaymentChargeOutcome.OutcomeUnknown,
            because: "an unexplained 500 may have charged the card, so the recovery sweep settles it");
        http.Requests.Should().ContainSingle(
            because: "the recovery sweep retries under the same idempotency key, not this request");
    }

    [Fact]
    public async Task Charge_maps_a_service_unavailable_answer_to_unavailable()
    {
        var result = await Charge(StubAdyenHttp.Returning(
            HttpStatusCode.ServiceUnavailable, "service unavailable"));

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Unavailable);
    }

    [Fact]
    public async Task Charge_maps_unusable_response_to_outcome_unknown()
    {
        var result = await Charge(StubAdyenHttp.Returning(
            HttpStatusCode.BadGateway, "opaque provider noise"));

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.OutcomeUnknown);
    }

    [Fact]
    public async Task Charge_maps_a_timeout_to_timeout()
    {
        var result = await Charge(new StubAdyenHttp(
            _ => throw new TaskCanceledException("The request timed out.")));

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Timeout);
    }

    [Fact]
    public async Task Charge_propagates_cancellation_when_caller_requested_it()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => Gateway(StubAdyenHttp.Returning(HttpStatusCode.OK, "{}")).ChargeAsync(
            Provider(), Request(), Guid.NewGuid().ToString(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Charge_maps_a_connection_failure_to_outcome_unknown()
    {
        var result = await Charge(new StubAdyenHttp(
            _ => throw new HttpRequestException("connection reset")));

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.OutcomeUnknown);
    }

    private static Task<StoredPaymentChargeProviderResult> Charge(StubAdyenHttp http) =>
        Gateway(http).ChargeAsync(
            Provider(), Request(), Guid.NewGuid().ToString(), CancellationToken.None);

    internal static CheckoutApiStoredPaymentChargeProviderGateway Gateway(IHttpClientFactory http)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions { ProviderTimeoutSeconds = 15 });

        return new CheckoutApiStoredPaymentChargeProviderGateway(
            http,
            new AdyenEndpointPolicy(),
            options.Object,
            NullLogger<CheckoutApiStoredPaymentChargeProviderGateway>.Instance);
    }

    private static PaymentProvider Provider() =>
        new()
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            ApiBaseUrl = "https://checkout-test.adyen.com/v72",
            ApiKey = "secret",
            MerchantId = "merchant"
        };

    private static StoredPaymentChargeRequest Request() =>
        new()
        {
            MerchantAccount = "merchant",
            Reference = $"c1.route.{Guid.NewGuid()}",
            Amount = new ProviderAmount { Currency = "EUR", Value = 1000 }
        };
}

/// <summary>Answers Adyen's <c>/payments</c> from a script and records every attempt.</summary>
internal sealed class StubAdyenHttp : HttpMessageHandler, IHttpClientFactory
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubAdyenHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        _respond = respond;

    public List<SentRequest> Requests { get; } = [];

    public static StubAdyenHttp Returning(HttpStatusCode status, string body) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

    public static StubAdyenHttp Json(object body) =>
        Returning(HttpStatusCode.OK, JsonSerializer.Serialize(body));

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Requests.Add(new SentRequest(
            request.Method,
            request.RequestUri!.AbsoluteUri,
            request.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase),
            request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken)));

        return _respond(request);
    }

    internal sealed record SentRequest(
        HttpMethod Method,
        string Url,
        Dictionary<string, string> Headers,
        string Body);
}
