using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Requests;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// The two Stripe Checkout clients own the mapping from Stripe's response shape
/// onto the outcomes the payment pipeline reasons about. Everything that
/// decides whether a payment stays recoverable lives here, so each branch gets
/// its own case.
/// </summary>
public sealed class StripeCheckoutClientTests
{
    private const string IdempotencyKey = "4b82f20f-d96b-4078-a686-bd27843fae02";

    private readonly Mock<IHttpService> _http = new();

    private static IOptionsMonitor<PaymentOptions> Options(int timeoutSeconds = 15)
    {
        var monitor = new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.SetupGet(x => x.CurrentValue)
            .Returns(new PaymentOptions { ProviderTimeoutSeconds = timeoutSeconds });

        return monitor.Object;
    }

    private StripeCheckoutSessionClient SessionClient(int timeoutSeconds = 15) =>
        new(
            _http.Object,
            new StripeEndpointPolicy(),
            Options(timeoutSeconds),
            NullLogger<StripeCheckoutSessionClient>.Instance);

    private StripeCheckoutResultClient ResultClient(int timeoutSeconds = 15) =>
        new(
            _http.Object,
            new StripeEndpointPolicy(),
            Options(timeoutSeconds),
            NullLogger<StripeCheckoutResultClient>.Instance);

    private static PaymentProvider Provider(
        string apiBaseUrl = "https://api.stripe.com") => new()
        {
            ProviderName = PaymentConstants.StripeProvider,
            ApiBaseUrl = apiBaseUrl,
            ApiKey = "sk_test_secret",
            MerchantId = "acct_1"
        };

    /// <summary>
    /// Built through the real factory so the form the client posts is the one
    /// Stripe would actually receive.
    /// </summary>
    private static ProviderInitiationRequest Request() =>
        new StripeInitiationRequestFactory().Create(
            new MakePaymentRequest
            {
                Description = "A description",
                CustomerEmail = "shopper@example.com"
            },
            new PaymentExecutionContext("tenant-1", "actor-1", "organization-1"),
            new PaymentDetail
            {
                ItemId = "payment-1",
                TenantId = "tenant-1",
                CurrencyCode = "EUR"
            },
            Provider(),
            "https://payments.example/return?state=signed",
            "payment-reference",
            "shopper-reference",
            null,
            includeStoredPaymentMethods: true,
            minorUnits: 2500);

    private void SetupCreate(
        StripeCheckoutSession? session,
        string error = "")
    {
        _http.Setup(x => x.SendFormUrlEncoded<StripeCheckoutSession>(
                It.IsAny<HttpMethod>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ReturnsAsync((session!, error));
    }

    private void SetupRead(
        StripeCheckoutSession? session,
        string error = "")
    {
        _http.Setup(x => x.SendRequest<StripeCheckoutSession>(
                It.IsAny<HttpMethod>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ReturnsAsync((session!, error));
    }

    [Fact]
    public void The_session_client_claims_stripe_only()
    {
        var client = SessionClient();

        client.Supports(PaymentConstants.StripeProvider).Should().BeTrue();
        client.Supports("STRIPE").Should().BeTrue();
        client.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeFalse();
    }

    [Fact]
    public void The_result_client_claims_stripe_only()
    {
        var client = ResultClient();

        client.Supports(PaymentConstants.StripeProvider).Should().BeTrue();
        client.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeFalse();
    }

    [Fact]
    public async Task Creating_a_session_posts_the_form_with_auth_and_idempotency()
    {
        using var source = new CancellationTokenSource();
        Dictionary<string, string>? headers = null;
        string? url = null;
        _http.Setup(x => x.SendFormUrlEncoded<StripeCheckoutSession>(
                HttpMethod.Post,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                source.Token,
                15))
            .Callback(
                (
                    HttpMethod _,
                    Dictionary<string, string> _,
                    string requestUrl,
                    Dictionary<string, string>? requestHeaders,
                    CancellationToken _,
                    int? _) =>
                {
                    url = requestUrl;
                    headers = requestHeaders;
                })
            .ReturnsAsync((
                new StripeCheckoutSession
                {
                    Id = "cs_test_1",
                    Url = "https://checkout.stripe.com/c/pay/cs_test_1",
                    ExpiresAt = 1_800_000_000
                },
                string.Empty));

        var result = await SessionClient().CreateSessionAsync(
            Provider(),
            Request(),
            IdempotencyKey,
            source.Token);

        result.Outcome.Should().Be(ProviderClientOutcome.Success);
        result.Response!.Id.Should().Be("cs_test_1");
        result.Response.Url.Should().Be("https://checkout.stripe.com/c/pay/cs_test_1");
        result.Response.ExpiresAt.Should().Be(
            DateTimeOffset.FromUnixTimeSeconds(1_800_000_000).UtcDateTime);
        url.Should().Be("https://api.stripe.com/v1/checkout/sessions");
        headers!["Authorization"].Should().Be("Bearer sk_test_secret");
        headers["Idempotency-Key"].Should().Be(IdempotencyKey);
    }

    [Fact]
    public async Task A_session_without_an_expiry_leaves_it_unset()
    {
        SetupCreate(new StripeCheckoutSession
        {
            Id = "cs_test_1",
            Url = "https://checkout.stripe.com/c/pay/cs_test_1"
        });

        var result = await SessionClient().CreateSessionAsync(
            Provider(),
            Request(),
            IdempotencyKey,
            CancellationToken.None);

        result.Response!.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task The_provider_timeout_is_clamped_into_stripes_supported_range()
    {
        int? observed = null;
        _http.Setup(x => x.SendFormUrlEncoded<StripeCheckoutSession>(
                It.IsAny<HttpMethod>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .Callback(
                (
                    HttpMethod _,
                    Dictionary<string, string> _,
                    string _,
                    Dictionary<string, string>? _,
                    CancellationToken _,
                    int? timeout) => observed = timeout)
            .ReturnsAsync(((StripeCheckoutSession)null!, string.Empty));

        await SessionClient(600).CreateSessionAsync(
            Provider(),
            Request(),
            IdempotencyKey,
            CancellationToken.None);

        observed.Should().Be(60);
    }

    [Fact]
    public async Task An_unsafe_provider_endpoint_is_refused_before_any_call()
    {
        var result = await SessionClient().CreateSessionAsync(
            Provider("https://127.0.0.1/v1"),
            Request(),
            IdempotencyKey,
            CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Unavailable);
        _http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Creating_a_session_requires_a_provider_and_a_request()
    {
        var client = SessionClient();

        await FluentActions.Awaiting(() => client.CreateSessionAsync(
                null!,
                Request(),
                IdempotencyKey,
                CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => client.CreateSessionAsync(
                Provider(),
                null!,
                IdempotencyKey,
                CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("api_error", ProviderClientOutcome.Unavailable)]
    [InlineData("rate_limit_error", ProviderClientOutcome.Unavailable)]
    [InlineData("invalid_request_error", ProviderClientOutcome.Rejected)]
    [InlineData("card_error", ProviderClientOutcome.Rejected)]
    [InlineData("something_new", ProviderClientOutcome.Failure)]
    public async Task A_stripe_error_on_create_maps_to_the_matching_outcome(
        string errorType,
        ProviderClientOutcome expected)
    {
        SetupCreate(new StripeCheckoutSession
        {
            Error = new StripeError { Type = errorType, Code = "some_code" }
        });

        var result = await SessionClient().CreateSessionAsync(
            Provider(),
            Request(),
            IdempotencyKey,
            CancellationToken.None);

        result.Outcome.Should().Be(expected);
        result.ProviderErrorCode.Should().Be("some_code");
    }

    [Fact]
    public async Task A_decline_code_is_preferred_over_the_generic_card_code()
    {
        SetupCreate(new StripeCheckoutSession
        {
            Error = new StripeError
            {
                Type = "card_error",
                Code = "card_declined",
                DeclineCode = "insufficient_funds"
            }
        });

        var result = await SessionClient().CreateSessionAsync(
            Provider(),
            Request(),
            IdempotencyKey,
            CancellationToken.None);

        result.ProviderErrorCode.Should().Be("insufficient_funds");
    }

    [Fact]
    public async Task A_package_validation_error_is_reported_as_a_rejection()
    {
        SetupCreate(
            null,
            "HTTP request failed with status code 422. Error: {\"status\":422,\"errorCode\":\"14_0408\",\"errorType\":\"validation\"}");

        var result = await SessionClient().CreateSessionAsync(
            Provider(),
            Request(),
            IdempotencyKey,
            CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Rejected);
        result.ProviderErrorCode.Should().Be("14_0408");
    }

    [Theory]
    [InlineData("Circuit is open")]
    [InlineData("Service unavailable")]
    public async Task A_transient_transport_failure_keeps_the_payment_recoverable(
        string packageError)
    {
        SetupCreate(null, packageError);

        var result = await SessionClient().CreateSessionAsync(
            Provider(),
            Request(),
            IdempotencyKey,
            CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Unavailable);
    }

    [Fact]
    public async Task An_unrecognised_transport_failure_is_terminal_and_never_echoed()
    {
        SetupCreate(null, "secret package failure with credentials");

        var result = await SessionClient().CreateSessionAsync(
            Provider(),
            Request(),
            IdempotencyKey,
            CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Failure);
        result.ProviderErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task A_half_built_session_is_treated_as_no_usable_response()
    {
        // An id without a url cannot be redirected to, so it is not a success.
        SetupCreate(new StripeCheckoutSession { Id = "cs_test_1" });

        var result = await SessionClient().CreateSessionAsync(
            Provider(),
            Request(),
            IdempotencyKey,
            CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Failure);
    }

    [Fact]
    public async Task An_internal_timeout_on_create_is_reported_as_a_timeout()
    {
        _http.Setup(x => x.SendFormUrlEncoded<StripeCheckoutSession>(
                It.IsAny<HttpMethod>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await SessionClient().CreateSessionAsync(
            Provider(),
            Request(),
            IdempotencyKey,
            CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Timeout);
    }

    [Fact]
    public async Task A_caller_cancellation_on_create_is_propagated()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        _http.Setup(x => x.SendFormUrlEncoded<StripeCheckoutSession>(
                It.IsAny<HttpMethod>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => SessionClient().CreateSessionAsync(
            Provider(),
            Request(),
            IdempotencyKey,
            source.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task An_unexpected_failure_on_create_is_terminal()
    {
        _http.Setup(x => x.SendFormUrlEncoded<StripeCheckoutSession>(
                It.IsAny<HttpMethod>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await SessionClient().CreateSessionAsync(
            Provider(),
            Request(),
            IdempotencyKey,
            CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Failure);
    }

    [Fact]
    public async Task Reading_a_result_escapes_the_session_id_into_the_path()
    {
        string? url = null;
        Dictionary<string, string>? headers = null;
        _http.Setup(x => x.SendRequest<StripeCheckoutSession>(
                HttpMethod.Get,
                It.IsAny<string>(),
                It.IsAny<object>(),
                "application/x-www-form-urlencoded",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .Callback(
                (
                    HttpMethod _,
                    string requestUrl,
                    object _,
                    string _,
                    Dictionary<string, string>? requestHeaders,
                    CancellationToken _,
                    int? _) =>
                {
                    url = requestUrl;
                    headers = requestHeaders;
                })
            .ReturnsAsync((
                new StripeCheckoutSession
                {
                    Id = "cs test/1",
                    Status = "complete",
                    PaymentStatus = "paid"
                },
                string.Empty));

        await ResultClient().GetAsync(
            Provider(),
            "cs test/1",
            "unused",
            CancellationToken.None);

        url.Should().Be("https://api.stripe.com/v1/checkout/sessions/cs%20test%2F1");
        headers!.Should().NotContainKey("Idempotency-Key");
    }

    [Fact]
    public async Task A_completed_paid_session_is_mapped_onto_the_shared_result()
    {
        SetupRead(new StripeCheckoutSession
        {
            Id = "cs_test_1",
            Status = "complete",
            PaymentStatus = "paid",
            ClientReferenceId = "payment-reference",
            PaymentIntent = "pi_1",
            AmountTotal = 2500,
            Currency = "eur"
        });

        var result = await ResultClient().GetAsync(
            Provider(),
            "cs_test_1",
            "unused",
            CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Success);
        result.Response!.Id.Should().Be("cs_test_1");
        result.Response.Reference.Should().Be("payment-reference");
        result.Response.Status.Should().Be(
            StripeCheckoutStatusMapper.Compose("complete", "paid"));
        result.Response.Amount!.Value.Should().Be(2500);
        result.Response.Amount.Currency.Should().Be("EUR");
        result.Response.Payments.Should().ContainSingle();
        result.Response.Payments[0].PspReference.Should().Be("pi_1");
        result.Response.Payments[0].ResultCode.Should().Be("paid");
    }

    [Fact]
    public async Task A_session_without_an_intent_carries_no_payments()
    {
        SetupRead(new StripeCheckoutSession
        {
            Id = "cs_test_1",
            Status = "open",
            PaymentStatus = "unpaid"
        });

        var result = await ResultClient().GetAsync(
            Provider(),
            "cs_test_1",
            "unused",
            CancellationToken.None);

        result.Response!.Payments.Should().BeEmpty();
        result.Response.Amount.Should().BeNull();
    }

    [Fact]
    public async Task An_amount_without_a_currency_is_dropped_rather_than_half_reported()
    {
        SetupRead(new StripeCheckoutSession
        {
            Id = "cs_test_1",
            Status = "complete",
            AmountTotal = 2500
        });

        var result = await ResultClient().GetAsync(
            Provider(),
            "cs_test_1",
            "unused",
            CancellationToken.None);

        result.Response!.Amount.Should().BeNull();
    }

    [Theory]
    [InlineData("api_error", ProviderClientOutcome.Unavailable)]
    [InlineData("invalid_request_error", ProviderClientOutcome.Rejected)]
    [InlineData("mystery", ProviderClientOutcome.Failure)]
    public async Task A_stripe_error_on_read_maps_to_the_matching_outcome(
        string errorType,
        ProviderClientOutcome expected)
    {
        SetupRead(new StripeCheckoutSession
        {
            Id = "cs_test_1",
            Error = new StripeError { Type = errorType }
        });

        var result = await ResultClient().GetAsync(
            Provider(),
            "cs_test_1",
            "unused",
            CancellationToken.None);

        result.Outcome.Should().Be(expected);
        result.Response.Should().BeNull();
    }

    [Fact]
    public async Task A_missing_session_id_is_refused_before_any_call()
    {
        var result = await ResultClient().GetAsync(
            Provider(),
            "   ",
            "unused",
            CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Unavailable);
        _http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_unsafe_endpoint_is_refused_before_reading_a_result()
    {
        var result = await ResultClient().GetAsync(
            Provider("http://169.254.169.254/latest"),
            "cs_test_1",
            "unused",
            CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Unavailable);
        _http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reading_a_result_requires_a_provider()
    {
        var act = () => ResultClient().GetAsync(
            null!,
            "cs_test_1",
            "unused",
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("Circuit is open", ProviderClientOutcome.Unavailable)]
    [InlineData("unrecognised transport failure", ProviderClientOutcome.Failure)]
    public async Task A_transport_failure_on_read_is_classified_by_transience(
        string packageError,
        ProviderClientOutcome expected)
    {
        SetupRead(null, packageError);

        var result = await ResultClient().GetAsync(
            Provider(),
            "cs_test_1",
            "unused",
            CancellationToken.None);

        result.Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task An_internal_timeout_on_read_is_reported_as_a_timeout()
    {
        _http.Setup(x => x.SendRequest<StripeCheckoutSession>(
                It.IsAny<HttpMethod>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await ResultClient().GetAsync(
            Provider(),
            "cs_test_1",
            "unused",
            CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Timeout);
    }

    [Fact]
    public async Task A_caller_cancellation_on_read_is_propagated()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        _http.Setup(x => x.SendRequest<StripeCheckoutSession>(
                It.IsAny<HttpMethod>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => ResultClient().GetAsync(
            Provider(),
            "cs_test_1",
            "unused",
            source.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task An_unexpected_failure_on_read_is_terminal()
    {
        _http.Setup(x => x.SendRequest<StripeCheckoutSession>(
                It.IsAny<HttpMethod>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await ResultClient().GetAsync(
            Provider(),
            "cs_test_1",
            "unused",
            CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Failure);
    }
}
