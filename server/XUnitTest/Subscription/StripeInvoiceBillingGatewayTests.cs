using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Services;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>Charging a renewal as a standalone Stripe Invoice.</summary>
public sealed class StripeInvoiceBillingGatewayTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<IPaymentProviderCache> _providers = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IStoredPaymentMethodRepository> _storedMethods = new();
    private readonly Mock<IProviderTokenProtector> _tokenProtector = new();
    private readonly Mock<IStripeInvoiceClient> _invoices = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero));

    public StripeInvoiceBillingGatewayTests()
    {
        _providers
            .Setup(cache => cache.GetAsync(
                TenantId,
                OrganizationId,
                PaymentConstants.StripeProvider,
                It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(Provider());

        _storedMethods
            .Setup(repository => repository.GetAsync(
                TenantId, "method-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewMethod());

        _storedMethods
            .Setup(repository => repository.TryClaimForPaymentAsync(
                TenantId,
                "method-1",
                "shopper-1",
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewMethod());

        _tokenProtector
            .Setup(protector => protector.UnprotectAsync(
                It.IsAny<StoredPaymentMethod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderTokenReadResult(true, "pm_456"));

        _invoices
            .Setup(client => client.CreateInvoiceItemAsync(
                It.IsAny<PaymentProvider>(),
                "cus_123",
                8_900,
                "CHF",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceCallResult(StripeInvoiceOutcome.Success, "ii_1"));

        _invoices
            .Setup(client => client.CreateInvoiceAsync(
                It.IsAny<PaymentProvider>(), "cus_123", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceCallResult(StripeInvoiceOutcome.Success, "in_1", "draft"));

        _invoices
            .Setup(client => client.FinalizeInvoiceAsync(
                It.IsAny<PaymentProvider>(), "in_1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceCallResult(StripeInvoiceOutcome.Success, "in_1", "open"));

        _invoices
            .Setup(client => client.PayInvoiceAsync(
                It.IsAny<PaymentProvider>(), "in_1", "pm_456", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceCallResult(StripeInvoiceOutcome.Success, "in_1", "paid"));
    }

    [Fact]
    public async Task A_full_success_returns_the_invoice_id()
    {
        var result = await Gateway().ChargeAsync(Request(), "idem-1", "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("in_1");
    }

    [Fact]
    public async Task Each_stripe_call_gets_its_own_suffixed_idempotency_key()
    {
        await Gateway().ChargeAsync(Request(), "sub-renew:sub-1:M20260901T000000Z:1", "corr-1", CancellationToken.None);

        _invoices.Verify(client => client.CreateInvoiceItemAsync(
            It.IsAny<PaymentProvider>(), "cus_123", 8_900, "CHF", It.IsAny<string>(),
            "sub-renew:sub-1:M20260901T000000Z:1:item", It.IsAny<CancellationToken>()));
        _invoices.Verify(client => client.PayInvoiceAsync(
            It.IsAny<PaymentProvider>(), "in_1", "pm_456",
            "sub-renew:sub-1:M20260901T000000Z:1:pay", It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_declined_payment_voids_the_invoice_and_is_reported_as_provider_rejected()
    {
        _invoices
            .Setup(client => client.PayInvoiceAsync(
                It.IsAny<PaymentProvider>(), "in_1", "pm_456", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceCallResult(
                StripeInvoiceOutcome.Rejected, "in_1", "open", "card_declined"));

        var result = await Gateway().ChargeAsync(Request(), "idem-1", "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.ProviderRejected);
        result.ErrorCode.Should().Be("card_declined");
        _invoices.Verify(
            client => client.VoidInvoiceAsync(It.IsAny<PaymentProvider>(), "in_1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task No_provider_customer_id_fails_closed_without_calling_stripe()
    {
        var request = Request();
        request.ProviderCustomerId = null;

        var result = await Gateway().ChargeAsync(request, "idem-1", "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("subscription_customer_unresolved");
        _invoices.Verify(
            client => client.CreateInvoiceItemAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_claim_conflict_is_a_conflict_failure()
    {
        _storedMethods
            .Setup(repository => repository.TryClaimForPaymentAsync(
                TenantId, "method-1", "shopper-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoredPaymentMethod?)null);

        var result = await Gateway().ChargeAsync(Request(), "idem-1", "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
    }

    [Fact]
    public async Task An_unreadable_token_is_unavailable()
    {
        _tokenProtector
            .Setup(protector => protector.UnprotectAsync(
                It.IsAny<StoredPaymentMethod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProviderTokenReadResult.Failed);

        var result = await Gateway().ChargeAsync(Request(), "idem-1", "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("stored_payment_method_token_unavailable");
    }

    [Fact]
    public async Task The_claim_is_always_released()
    {
        await Gateway().ChargeAsync(Request(), "idem-1", "corr-1", CancellationToken.None);

        _storedMethods.Verify(
            repository => repository.ReleasePaymentClaimAsync(
                TenantId, "method-1", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private StripeInvoiceBillingGateway Gateway() => new(
        _providers.Object,
        _payments.Object,
        _storedMethods.Object,
        _tokenProtector.Object,
        _invoices.Object,
        NullLogger<StripeInvoiceBillingGateway>.Instance,
        _time);

    private static SubscriptionChargeRequest Request() => new()
    {
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        ProviderName = PaymentConstants.StripeProvider,
        StoredPaymentMethodId = "method-1",
        ProviderCustomerId = "cus_123",
        AmountMinor = 8_900,
        CurrencyCode = "CHF",
        OrderId = "sub:sub-1:M20260901T000000Z",
        Description = "Professional renewal"
    };

    private static PaymentProvider Provider() => new()
    {
        ProviderName = PaymentConstants.StripeProvider,
        ApiBaseUrl = StripeConstants.ApiBaseUrl,
        ApiKey = "secret",
        IsEnabled = true
    };

    private static StoredPaymentMethod NewMethod() => new()
    {
        ItemId = "method-1",
        TenantId = TenantId,
        ShopperReference = "shopper-1"
    };
}
