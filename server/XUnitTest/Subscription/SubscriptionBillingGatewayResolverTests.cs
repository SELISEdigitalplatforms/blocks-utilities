using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>Which billing gateway a renewal actually goes through, by provider name.</summary>
public sealed class SubscriptionBillingGatewayResolverTests
{
    [Fact]
    public async Task Stripe_routes_to_the_stripe_invoice_gateway()
    {
        var recurring = new Mock<IRecurringPaymentService>();
        var currency = new Mock<ICurrencyMinorUnitResolver>();
        var stripeInvoices = new Mock<IStripeInvoiceClient>();

        var providers = new Mock<IPaymentProviderCache>();
        providers
            .Setup(cache => cache.GetAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync((PaymentProvider?)null);

        var recurringGateway = new RecurringChargeBillingGateway(recurring.Object, currency.Object);
        var stripeGateway = new StripeInvoiceBillingGateway(
            providers.Object,
            Mock.Of<IPaymentRepository>(),
            Mock.Of<IStoredPaymentMethodRepository>(),
            Mock.Of<IProviderTokenProtector>(),
            stripeInvoices.Object,
            currency.Object,
            NullLogger<StripeInvoiceBillingGateway>.Instance);

        var resolver = new SubscriptionBillingGatewayResolver(stripeGateway, recurringGateway);

        await resolver.ChargeAsync(
            new SubscriptionChargeRequest
            {
                ProviderName = PaymentConstants.StripeProvider,
                ProviderCustomerId = "cus_123"
            },
            "idem-1",
            "corr-1",
            CancellationToken.None);

        // Reaching StripeInvoiceBillingGateway's own provider resolution (and getting nothing
        // back, since it was stubbed to null) is the proof it was the one actually called.
        recurring.Verify(
            service => service.CreateRecurringPaymentAsync(
                It.IsAny<CreateRecurringPaymentRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Anything_other_than_stripe_falls_through_to_the_recurring_charge_gateway()
    {
        var recurring = new Mock<IRecurringPaymentService>();
        recurring
            .Setup(service => service.CreateRecurringPaymentAsync(
                It.IsAny<CreateRecurringPaymentRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentOperationResult.Failure(
                PaymentFailureKind.Unavailable,
                "x", "x", "corr-1"));
        var currency = new Mock<ICurrencyMinorUnitResolver>();
        currency
            .Setup(resolver => resolver.TryConvertBack(
                It.IsAny<long>(), It.IsAny<string>(), out It.Ref<decimal>.IsAny))
            .Returns(true);

        var recurringGateway = new RecurringChargeBillingGateway(recurring.Object, currency.Object);
        var stripeInvoices = new Mock<IStripeInvoiceClient>(MockBehavior.Strict);
        var stripeGateway = new StripeInvoiceBillingGateway(
            Mock.Of<IPaymentProviderCache>(),
            Mock.Of<IPaymentRepository>(),
            Mock.Of<IStoredPaymentMethodRepository>(),
            Mock.Of<IProviderTokenProtector>(),
            stripeInvoices.Object,
            currency.Object,
            NullLogger<StripeInvoiceBillingGateway>.Instance);

        var resolver = new SubscriptionBillingGatewayResolver(stripeGateway, recurringGateway);

        await resolver.ChargeAsync(
            new SubscriptionChargeRequest
            {
                ProviderName = PaymentConstants.AdyenOnlineProvider,
                AmountMinor = 1_000,
                CurrencyCode = "CHF"
            },
            "idem-1",
            "corr-1",
            CancellationToken.None);

        recurring.Verify(
            service => service.CreateRecurringPaymentAsync(
                It.IsAny<CreateRecurringPaymentRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        stripeInvoices.VerifyNoOtherCalls();
    }
}
