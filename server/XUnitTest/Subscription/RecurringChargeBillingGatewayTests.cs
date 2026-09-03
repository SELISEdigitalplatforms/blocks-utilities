using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// Charging a renewal through the provider-neutral recurring-payment stack -- the path every
/// provider other than Stripe's own Invoice API takes, Adyen included.
/// </summary>
/// <remarks>
/// Closes a real gap found while auditing this PR: the class-level remark on
/// <see cref="RecurringChargeBillingGateway"/> asserted this path is "provider-neutral by
/// construction", which is true of <em>whether</em> it charges, but the subscription invoice
/// breakdown (gross, discounts, credit, tax, service period) was silently dropped on the floor
/// between <see cref="SubscriptionChargeRequest"/> and <see cref="CreateRecurringPaymentRequest"/>
/// -- present on the former, absent on the latter. An Adyen-routed renewal recorded a payment with
/// none of the figures its own invoice needed. These tests pin the fix: the full breakdown now
/// survives the crossing, for Adyen exactly as for any other non-Stripe-invoice provider.
/// </remarks>
public sealed class RecurringChargeBillingGatewayTests
{
    private const string AdyenProvider = "ADYEN-ONLINE";

    private readonly Mock<IRecurringPaymentService> _recurringPayments = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _currency = new();

    public RecurringChargeBillingGatewayTests()
    {
        _currency
            .Setup(resolver => resolver.TryConvertBack(90_000, "CHF", out It.Ref<decimal>.IsAny))
            .Returns((long _, string _, out decimal amount) =>
            {
                amount = 900.00m;
                return true;
            });
    }

    private RecurringChargeBillingGateway Gateway() =>
        new(_recurringPayments.Object, _currency.Object);

    private static SubscriptionChargeRequest Request() => new()
    {
        TenantId = "tenant-1",
        OrganizationId = "org-1",
        SubscriberOrganizationId = "org-subscriber",
        ProviderName = AdyenProvider,
        StoredPaymentMethodId = "method-1",
        AmountMinor = 90_000,
        CurrencyCode = "CHF",
        OrderId = "order-1",
        Description = "Professional renewal",
        NetAmountMinor = 83_640,
        TaxAmountMinor = 6_360,
        TaxRateBasisPoints = 770,
        TaxMode = TaxMode.Exclusive,
        CreditConsumedMinor = 1_500,
        GrossAmountMinor = 100_000,
        BuiltInDiscountMinor = 8_000,
        PromotionalDiscountMinor = 9_200,
        AutomaticDiscountBasisPoints = 800,
        QuantityDiscountBasisPoints = 500,
        DiscountCombination = "Additive"
    };

    [Fact]
    public async Task An_Adyen_renewal_carries_the_full_subscription_breakdown_across()
    {
        CreateRecurringPaymentRequest? captured = null;
        _recurringPayments
            .Setup(service => service.CreateRecurringPaymentAsync(
                It.IsAny<CreateRecurringPaymentRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback((CreateRecurringPaymentRequest request, string _, string _, CancellationToken _) =>
                captured = request)
            .ReturnsAsync(PaymentOperationResult.Success(
                new PaymentResponse { PaymentDetailId = "payment-1" }, "corr-1"));

        var result = await Gateway().ChargeAsync(Request(), "idem-1", "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.ProviderName.Should().Be(AdyenProvider);

        var breakdown = captured.SubscriptionInvoiceBreakdown;
        breakdown.Should().NotBeNull();
        breakdown!.NetAmountMinor.Should().Be(83_640);
        breakdown.TaxAmountMinor.Should().Be(6_360);
        breakdown.TaxRateBasisPoints.Should().Be(770);
        breakdown.TaxMode.Should().Be("Exclusive");
        breakdown.CreditConsumedMinor.Should().Be(1_500);
        breakdown.GrossAmountMinor.Should().Be(100_000);
        breakdown.BuiltInDiscountMinor.Should().Be(8_000);
        breakdown.PromotionalDiscountMinor.Should().Be(9_200);
        breakdown.AutomaticDiscountBasisPoints.Should().Be(800);
        breakdown.QuantityDiscountBasisPoints.Should().Be(500);
        breakdown.DiscountCombination.Should().Be("Additive");
    }

    [Fact]
    public async Task An_untaxed_Adyen_charge_records_no_tax_mode_rather_than_a_default_one()
    {
        CreateRecurringPaymentRequest? captured = null;
        _recurringPayments
            .Setup(service => service.CreateRecurringPaymentAsync(
                It.IsAny<CreateRecurringPaymentRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback((CreateRecurringPaymentRequest request, string _, string _, CancellationToken _) =>
                captured = request)
            .ReturnsAsync(PaymentOperationResult.Success(
                new PaymentResponse { PaymentDetailId = "payment-1" }, "corr-1"));

        var request = Request();
        request.TaxAmountMinor = 0;
        request.TaxRateBasisPoints = null;
        request.NetAmountMinor = request.AmountMinor;

        await Gateway().ChargeAsync(request, "idem-1", "corr-1", CancellationToken.None);

        captured!.SubscriptionInvoiceBreakdown!.TaxMode.Should().BeNull();
    }

    [Fact]
    public async Task A_settlement_charge_carries_the_settlement_breakdown_instead_of_the_flat_one()
    {
        CreateRecurringPaymentRequest? captured = null;
        _recurringPayments
            .Setup(service => service.CreateRecurringPaymentAsync(
                It.IsAny<CreateRecurringPaymentRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback((CreateRecurringPaymentRequest request, string _, string _, CancellationToken _) =>
                captured = request)
            .ReturnsAsync(PaymentOperationResult.Success(
                new PaymentResponse { PaymentDetailId = "payment-1" }, "corr-1"));

        var request = Request();
        request.GrossAmountMinor = 0;
        request.BuiltInDiscountMinor = 0;
        request.PromotionalDiscountMinor = 0;
        request.Settlement = new SubscriptionSettlementBreakdown
        {
            Outgoing = new SubscriptionSettlementSide { ProratedValueMinor = 495 },
            Target = new SubscriptionSettlementSide { ProratedValueMinor = 1_012 },
            NetSettlementMinor = 517
        };

        await Gateway().ChargeAsync(request, "idem-1", "corr-1", CancellationToken.None);

        var breakdown = captured!.SubscriptionInvoiceBreakdown!;
        breakdown.Settlement.Should().NotBeNull();
        breakdown.Settlement!.NetSettlementMinor.Should().Be(517);
        breakdown.Settlement.Outgoing.ProratedValueMinor.Should().Be(495);
        breakdown.Settlement.Target.ProratedValueMinor.Should().Be(1_012);
    }
}
