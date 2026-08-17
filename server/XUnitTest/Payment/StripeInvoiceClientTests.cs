using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>Raw Stripe Invoice API calls: one standalone invoice per attempt, no Subscription object.</summary>
public sealed class StripeInvoiceClientTests
{
    [Fact]
    public async Task Creating_an_invoice_item_sends_the_amount_in_minor_units()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        http.Setup(service => service.SendFormUrlEncoded<StripeInvoice>(
                HttpMethod.Post,
                It.Is<Dictionary<string, string>>(form =>
                    form["customer"] == "cus_123" &&
                    form["amount"] == "8900" &&
                    form["currency"] == "chf" &&
                    form["description"] == "Professional renewal"),
                "https://api.stripe.com/v1/invoiceitems",
                It.Is<Dictionary<string, string>>(headers => headers["Idempotency-Key"] == "idem-1"),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>()))
            .ReturnsAsync((new StripeInvoice { Id = "ii_1" }, (string?)null));

        var result = await Client(http.Object).CreateInvoiceItemAsync(
            Provider(), "cus_123", 8_900, "CHF", "Professional renewal", "idem-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.InvoiceOrItemId.Should().Be("ii_1");
    }

    [Fact]
    public async Task Creating_an_invoice_never_lets_stripe_advance_it_on_its_own()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        http.Setup(service => service.SendFormUrlEncoded<StripeInvoice>(
                HttpMethod.Post,
                It.Is<Dictionary<string, string>>(form =>
                    form["customer"] == "cus_123" &&
                    form["collection_method"] == "charge_automatically" &&
                    form["auto_advance"] == "false" &&
                    form["default_payment_method"] == "pm_456"),
                "https://api.stripe.com/v1/invoices",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>()))
            .ReturnsAsync((new StripeInvoice { Id = "in_1", Status = "draft" }, (string?)null));

        var result = await Client(http.Object).CreateInvoiceAsync(
            Provider(), "cus_123", "pm_456", "idem-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Paying_an_invoice_that_stays_open_is_a_rejection_not_a_transport_failure()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripeInvoice>(
                It.IsAny<HttpMethod>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>()))
            .ReturnsAsync((new StripeInvoice { Id = "in_1", Status = "open" }, (string?)null));

        var result = await Client(http.Object).PayInvoiceAsync(
            Provider(), "in_1", "pm_456", "idem-1:pay", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.InvoiceOrItemId.Should().Be("in_1");
        result.SafeErrorCode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Paying_an_invoice_that_lands_paid_is_a_success()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripeInvoice>(
                It.IsAny<HttpMethod>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>()))
            .ReturnsAsync((new StripeInvoice { Id = "in_1", Status = "paid" }, (string?)null));

        var result = await Client(http.Object).PayInvoiceAsync(
            Provider(), "in_1", "pm_456", "idem-1:pay", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_card_error_from_stripe_is_a_rejection()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripeInvoice>(
                It.IsAny<HttpMethod>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>()))
            .ReturnsAsync((
                new StripeInvoice
                {
                    Error = new StripeError { Type = "card_error", DeclineCode = "insufficient_funds" }
                },
                (string?)null));

        var result = await Client(http.Object).PayInvoiceAsync(
            Provider(), "in_1", "pm_456", "idem-1:pay", CancellationToken.None);

        result.Outcome.Should().Be(StripeInvoiceOutcome.Rejected);
        result.SafeErrorCode.Should().Be("insufficient_funds");
    }

    [Fact]
    public async Task A_stripe_side_error_stays_recoverable()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripeInvoice>(
                It.IsAny<HttpMethod>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>()))
            .ReturnsAsync((
                new StripeInvoice { Error = new StripeError { Type = "api_error" } },
                (string?)null));

        var result = await Client(http.Object).FinalizeInvoiceAsync(
            Provider(), "in_1", "idem-1:finalize", CancellationToken.None);

        result.Outcome.Should().Be(StripeInvoiceOutcome.Unavailable);
    }

    [Fact]
    public async Task An_unsafe_provider_endpoint_is_refused_without_calling_http()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var provider = Provider();
        provider.ApiBaseUrl = "https://127.0.0.1";

        var result = await Client(http.Object).CreateInvoiceItemAsync(
            provider, "cus_123", 1_000, "CHF", "x", "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(StripeInvoiceOutcome.Unavailable);
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_timeout_reports_an_unknown_outcome_rather_than_a_failure()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripeInvoice>(
                It.IsAny<HttpMethod>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await Client(http.Object).PayInvoiceAsync(
            Provider(), "in_1", "pm_456", "idem-1:pay", CancellationToken.None);

        result.Outcome.Should().Be(StripeInvoiceOutcome.Timeout);
    }

    [Fact]
    public async Task Voiding_swallows_its_own_failure()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripeInvoice>(
                It.IsAny<HttpMethod>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Client(http.Object)
            .Invoking(client => client.VoidInvoiceAsync(Provider(), "in_1", CancellationToken.None))
            .Should().NotThrowAsync("a void failing must never mask the decline that caused it");
    }

    private static StripeInvoiceClient Client(IHttpService http) =>
        new(http, new StripeEndpointPolicy(), Options(), NullLogger<StripeInvoiceClient>.Instance);

    private static IOptionsMonitor<PaymentOptions> Options()
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions { ProviderTimeoutSeconds = 15 });

        return options.Object;
    }

    private static PaymentProvider Provider() =>
        new()
        {
            ProviderName = PaymentConstants.StripeProvider,
            ApiBaseUrl = StripeConstants.ApiBaseUrl,
            ApiKey = "secret",
            MerchantId = "merchant"
        };
}
