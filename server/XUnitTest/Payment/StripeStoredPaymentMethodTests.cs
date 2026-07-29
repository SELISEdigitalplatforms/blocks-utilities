using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class StripeStoredPaymentMethodTests
{
    [Fact]
    public void Gateway_supports_only_stripe()
    {
        var gateway = Gateway(Mock.Of<IHttpService>());

        gateway.Supports(PaymentConstants.StripeProvider).Should().BeTrue();
        gateway.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeFalse();
    }

    [Fact]
    public async Task Remove_detaches_the_payment_method()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        http.Setup(service => service.SendFormUrlEncoded<StripePaymentMethod>(
                HttpMethod.Post,
                It.Is<Dictionary<string, string>>(form => form.Count == 0),
                "https://api.stripe.com/v1/payment_methods/pm_1/detach",
                It.Is<Dictionary<string, string>>(headers =>
                    headers["Authorization"] == "Bearer secret" &&
                    !headers.ContainsKey("Idempotency-Key")),
                It.IsAny<CancellationToken>(),
                15))
            .ReturnsAsync((new StripePaymentMethod { Id = "pm_1" }, string.Empty));

        var outcome = await Gateway(http.Object).RemoveAsync(
            Provider(), Method(), "pm_1", CancellationToken.None);

        outcome.Should().Be(StoredPaymentMethodRemovalOutcome.Removed);
        http.VerifyAll();
    }

    /// <summary>
    /// Detaching something already detached is the state the caller asked for, so it must not
    /// be retried forever as a failure.
    /// </summary>
    [Fact]
    public async Task Remove_treats_a_missing_method_as_removed()
    {
        var outcome = await Gateway(Returning(
                new StripePaymentMethod
                {
                    Error = new StripeError
                    {
                        Type = "invalid_request_error",
                        Code = "resource_missing"
                    }
                },
                string.Empty))
            .RemoveAsync(Provider(), Method(), "pm_1", CancellationToken.None);

        outcome.Should().Be(StoredPaymentMethodRemovalOutcome.Removed);
    }

    [Fact]
    public async Task Remove_treats_an_unattached_method_as_removed()
    {
        var outcome = await Gateway(Returning(
                new StripePaymentMethod
                {
                    Error = new StripeError
                    {
                        Type = "invalid_request_error",
                        Message =
                            "The payment method you provided is not attached to a customer."
                    }
                },
                string.Empty))
            .RemoveAsync(Provider(), Method(), "pm_1", CancellationToken.None);

        outcome.Should().Be(StoredPaymentMethodRemovalOutcome.Removed);
    }

    [Fact]
    public async Task Remove_treats_bad_credentials_as_an_operational_failure()
    {
        var outcome = await Gateway(Returning(
                new StripePaymentMethod
                {
                    Error = new StripeError { Type = "authentication_error" }
                },
                string.Empty))
            .RemoveAsync(Provider(), Method(), "pm_1", CancellationToken.None);

        outcome.Should().Be(StoredPaymentMethodRemovalOutcome.OperationalFailure);
    }

    [Fact]
    public async Task Remove_leaves_an_unusable_response_unknown_so_it_is_retried()
    {
        var outcome = await Gateway(Returning(null, "gateway timeout"))
            .RemoveAsync(Provider(), Method(), "pm_1", CancellationToken.None);

        outcome.Should().Be(StoredPaymentMethodRemovalOutcome.OutcomeUnknown);
    }

    [Fact]
    public async Task Remove_rejects_unsafe_provider_endpoint_without_calling_http()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var provider = Provider();
        provider.ApiBaseUrl = "https://attacker.example";

        var outcome = await Gateway(http.Object).RemoveAsync(
            provider, Method(), "pm_1", CancellationToken.None);

        outcome.Should().Be(StoredPaymentMethodRemovalOutcome.OperationalFailure);
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Remove_rejects_an_empty_token_without_calling_http()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);

        var outcome = await Gateway(http.Object).RemoveAsync(
            Provider(), Method(), string.Empty, CancellationToken.None);

        outcome.Should().Be(StoredPaymentMethodRemovalOutcome.OperationalFailure);
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Remove_propagates_cancellation_when_caller_requested_it()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripePaymentMethod>(
                It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => Gateway(http.Object).RemoveAsync(
            Provider(), Method(), "pm_1", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Detail_lookup_reads_the_card_for_display()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        http.Setup(service => service.SendRequest<StripePaymentMethod>(
                HttpMethod.Get,
                "https://api.stripe.com/v1/payment_methods/pm_1",
                null!,
                "application/x-www-form-urlencoded",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                15))
            .ReturnsAsync((new StripePaymentMethod
            {
                Id = "pm_1",
                Type = "card",
                Card = new StripeCard
                {
                    Brand = "visa",
                    LastFour = "4242",
                    ExpiryMonth = 4,
                    ExpiryYear = 2030,
                    Funding = "credit",
                    Country = "US"
                }
            }, string.Empty));

        var detail = await DetailGateway(http.Object).GetAsync(
            Provider(), "pm_1", CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Brand.Should().Be("visa");
        detail.LastFour.Should().Be("4242");
        // Stripe reports expiry as numbers; a single-digit month must not lose its padding.
        detail.ExpiryMonth.Should().Be("04");
        detail.ExpiryYear.Should().Be("2030");
        detail.FundingSource.Should().Be("credit");
        detail.IssuerCountry.Should().Be("US");
        http.VerifyAll();
    }

    /// <summary>
    /// Card details are cosmetic; the card itself is not. A failed lookup returns nothing so
    /// the caller stores the card regardless.
    /// </summary>
    [Fact]
    public async Task Detail_lookup_returns_nothing_when_stripe_cannot_be_read()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendRequest<StripePaymentMethod>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var detail = await DetailGateway(http.Object).GetAsync(
            Provider(), "pm_1", CancellationToken.None);

        detail.Should().BeNull();
    }

    [Fact]
    public async Task Detail_lookup_rejects_unsafe_provider_endpoint_without_calling_http()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var provider = Provider();
        provider.ApiBaseUrl = "https://attacker.example";

        var detail = await DetailGateway(http.Object).GetAsync(
            provider, "pm_1", CancellationToken.None);

        detail.Should().BeNull();
        http.VerifyNoOtherCalls();
    }

    private static StripeStoredPaymentMethodDetailGateway DetailGateway(IHttpService http)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions { ProviderTimeoutSeconds = 15 });

        return new StripeStoredPaymentMethodDetailGateway(
            http,
            new StripeEndpointPolicy(),
            options.Object,
            NullLogger<StripeStoredPaymentMethodDetailGateway>.Instance);
    }

    private static IHttpService Returning(StripePaymentMethod? method, string error)
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripePaymentMethod>(
                It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync((method!, error));

        return http.Object;
    }

    private static StripeStoredPaymentMethodProviderGateway Gateway(IHttpService http)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions { ProviderTimeoutSeconds = 15 });

        return new StripeStoredPaymentMethodProviderGateway(
            http,
            new StripeEndpointPolicy(),
            options.Object,
            NullLogger<StripeStoredPaymentMethodProviderGateway>.Instance);
    }

    private static StoredPaymentMethod Method() =>
        new()
        {
            ProviderName = PaymentConstants.StripeProvider,
            ShopperReference = "shopper-1",
            StoredPaymentMethodToken = "pm_1"
        };

    private static PaymentProvider Provider() =>
        new()
        {
            ProviderName = PaymentConstants.StripeProvider,
            ApiBaseUrl = StripeConstants.ApiBaseUrl,
            ApiKey = "secret",
            MerchantId = "merchant"
        };
}
