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
using Subscription.DomainService.Utilities;
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
    private readonly Mock<ICurrencyMinorUnitResolver> _amounts = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero));

    public StripeInvoiceBillingGatewayTests()
    {
        _amounts
            .Setup(resolver => resolver.TryConvertBack(8_900, "CHF", out It.Ref<decimal>.IsAny))
            .Returns((long _, string _, out decimal amount) =>
            {
                amount = 89.00m;
                return true;
            });

        _payments
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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
                "in_1",
                It.IsAny<long>(),
                "CHF",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceCallResult(StripeInvoiceOutcome.Success, "ii_1"));

        _invoices
            .Setup(client => client.CreateInvoiceAsync(
                It.IsAny<PaymentProvider>(), "cus_123", "pm_456", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceCallResult(StripeInvoiceOutcome.Success, "in_1", "draft"));

        _invoices
            .Setup(client => client.FinalizeInvoiceAsync(
                It.IsAny<PaymentProvider>(), "in_1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceCallResult(
                StripeInvoiceOutcome.Success, "in_1", "open", AmountMinor: 8_900));

        _invoices
            .Setup(client => client.PayInvoiceAsync(
                It.IsAny<PaymentProvider>(), "in_1", "pm_456", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceCallResult(StripeInvoiceOutcome.Success, "in_1", "paid"));
    }

    [Fact]
    public async Task A_full_success_records_the_payment_and_returns_its_id()
    {
        PaymentDetail? recorded = null;
        _payments
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()))
            .Callback((PaymentDetail payment, CancellationToken _) => recorded = payment)
            .ReturnsAsync(true);

        var result = await Gateway().ChargeAsync(Request(), "idem-1", "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // The invoice id used to come back here, where a payment id was expected — which is why
        // renewals never reached the payment portal.
        recorded.Should().NotBeNull();
        result.Value.Should().Be(recorded!.ItemId);
        result.Value.Should().NotBe("in_1");
    }

    [Fact]
    public async Task The_recorded_payment_carries_what_reconciliation_needs()
    {
        PaymentDetail? recorded = null;
        _payments
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()))
            .Callback((PaymentDetail payment, CancellationToken _) => recorded = payment)
            .ReturnsAsync(true);

        var request = Request();
        request.SubscriberOrganizationId = "org-subscriber";

        await Gateway().ChargeAsync(request, "idem-1", "corr-1", CancellationToken.None);

        recorded.Should().NotBeNull();
        recorded!.PaymentStatus.Should().Be(PaymentStatuses.Captured);
        recorded.PaymentFlow.Should().Be(PaymentFlows.SubscriptionInvoice);
        recorded.PreciseAmount.Should().Be(89.00m);
        recorded.CurrencyCode.Should().Be("CHF");
        recorded.ProviderInvoiceId.Should().Be("in_1");
        recorded.OrderId.Should().Be(request.OrderId);

        // The merchant's scope settles it; the subscriber is who the revenue belongs to.
        recorded.OrganizationId.Should().Be(OrganizationId);
        recorded.CustomerOrganizationId.Should().Be("org-subscriber");
    }

    [Fact]
    public async Task A_settled_invoice_that_cannot_be_recorded_still_reports_the_renewal_paid()
    {
        // The money has moved. Reporting a failure here would have the next dunning attempt
        // charge the customer again over a bookkeeping problem.
        _payments
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("mongo unreachable"));

        var result = await Gateway().ChargeAsync(Request(), "idem-1", "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("in_1");
    }

    [Fact]
    public async Task The_settled_payment_is_recorded_under_the_shared_settlement_key()
    {
        // The contract the recovery sweep reads back. Spelled from the same helper at both ends:
        // two spellings of this name is how a paid-for quantity increase gets released as unpaid.
        PaymentDetail? recorded = null;
        _payments
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()))
            .Callback((PaymentDetail payment, CancellationToken _) => recorded = payment)
            .ReturnsAsync(true);

        var key = SubscriptionConstants.SettlementChargeKeyFor("sub-1", "claim-1");

        await Gateway().ChargeAsync(Request(), key, "corr-1", CancellationToken.None);

        recorded!.IdempotencyKey.Should().Be(SubscriptionConstants.RecordedSettlementKeyFor(key));
        recorded.IdempotencyKey.Should().NotBe(key, "the charge attempt reserved that one");
    }

    [Fact]
    public async Task A_replayed_settlement_points_at_the_payment_already_recorded()
    {
        _payments
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _payments
            .Setup(repository => repository.GetByIdempotencyKeyAsync(
                TenantId, "idem-1:settled", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail { ItemId = "pay-existing" });

        var result = await Gateway().ChargeAsync(Request(), "idem-1", "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("pay-existing");
    }

    [Fact]
    public async Task Each_stripe_call_gets_its_own_suffixed_idempotency_key()
    {
        await Gateway().ChargeAsync(Request(), "sub-renew:sub-1:M20260901T000000Z:1", "corr-1", CancellationToken.None);

        _invoices.Verify(client => client.CreateInvoiceItemAsync(
            It.IsAny<PaymentProvider>(), "cus_123", "in_1", 8_900, "CHF", It.IsAny<string>(),
            "sub-renew:sub-1:M20260901T000000Z:1:item", It.IsAny<CancellationToken>()));
        _invoices.Verify(client => client.PayInvoiceAsync(
            It.IsAny<PaymentProvider>(), "in_1", "pm_456",
            "sub-renew:sub-1:M20260901T000000Z:1:pay", It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task An_invoice_already_paid_by_finalizing_succeeds_without_paying_again()
    {
        // What a charge_automatically invoice actually does: auto_advance withholds Stripe's
        // retry schedule, not the collection at finalization.
        _invoices
            .Setup(client => client.FinalizeInvoiceAsync(
                It.IsAny<PaymentProvider>(), "in_1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceCallResult(
                StripeInvoiceOutcome.Success, "in_1", "paid", AmountMinor: 8_900));

        var result = await Gateway().ChargeAsync(Request(), "idem-1", "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Collected at finalization still has to be booked, or this path would advance the
        // period with no payment record behind it.
        _payments.Verify(
            repository => repository.TryCreateAsync(
                It.Is<PaymentDetail>(payment => payment.ProviderInvoiceId == "in_1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _invoices.Verify(
            client => client.PayInvoiceAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _invoices.Verify(
            client => client.VoidInvoiceAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_pay_call_rejected_because_the_invoice_is_already_paid_is_not_a_decline()
    {
        _invoices
            .Setup(client => client.PayInvoiceAsync(
                It.IsAny<PaymentProvider>(), "in_1", "pm_456", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceCallResult(
                StripeInvoiceOutcome.Rejected, "in_1", "paid", "invoice_already_paid"));

        var result = await Gateway().ChargeAsync(Request(), "idem-1", "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _payments.Verify(
            repository => repository.TryCreateAsync(
                It.Is<PaymentDetail>(payment => payment.ProviderInvoiceId == "in_1"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Voiding a settled invoice is the one thing that must never follow from this.
        _invoices.Verify(
            client => client.VoidInvoiceAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task An_invoice_finalized_for_nothing_is_abandoned_rather_than_credited()
    {
        // What a dropped line item looks like from here: finalized, owing nothing, and therefore
        // reported by Stripe as paid. Crediting it would advance a billing period for free.
        _invoices
            .Setup(client => client.FinalizeInvoiceAsync(
                It.IsAny<PaymentProvider>(), "in_1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceCallResult(
                StripeInvoiceOutcome.Success, "in_1", "paid", AmountMinor: 0));

        var result = await Gateway().ChargeAsync(Request(), "idem-1", "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_invoice_amount_mismatch");
        _invoices.Verify(
            client => client.VoidInvoiceAsync(
                It.IsAny<PaymentProvider>(), "in_1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task The_charged_line_is_attached_to_the_invoice_it_belongs_to()
    {
        await Gateway().ChargeAsync(Request(), "idem-1", "corr-1", CancellationToken.None);

        // Left pending instead, Stripe's current default omits it and the invoice is for nothing.
        _invoices.Verify(
            client => client.CreateInvoiceItemAsync(
                It.IsAny<PaymentProvider>(), "cus_123", "in_1", 8_900, "CHF",
                It.IsAny<string>(), "idem-1:item", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task The_invoice_names_the_card_the_billing_account_recorded()
    {
        await Gateway().ChargeAsync(Request(), "idem-1", "corr-1", CancellationToken.None);

        // Without this Stripe collects on the customer's own default card at finalization,
        // which need not be the one this renewal resolved.
        _invoices.Verify(
            client => client.CreateInvoiceAsync(
                It.IsAny<PaymentProvider>(), "cus_123", "pm_456", "CHF", "idem-1:invoice",
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the currency must reach the invoice, or its line item cannot attach to it");
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
                It.IsAny<PaymentProvider>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
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
        _amounts.Object,
        NullLogger<StripeInvoiceBillingGateway>.Instance,
        _time);

    [Fact]
    public async Task A_taxed_renewal_is_invoiced_as_a_subtotal_and_a_tax_line()
    {
        // What a subscriber downloading the invoice needs to see. This module calculated the tax, so
        // the lines are ours to state — Stripe is only being asked to show them.
        await Gateway().ChargeAsync(Taxed(), "key-1", "corr-1", CancellationToken.None);

        _invoices.Verify(
            client => client.CreateInvoiceItemAsync(
                It.IsAny<PaymentProvider>(), "cus_123", "in_1", 8_264, "CHF",
                "Professional renewal", "key-1:item", It.IsAny<CancellationToken>()),
            Times.Once);

        _invoices.Verify(
            client => client.CreateInvoiceItemAsync(
                It.IsAny<PaymentProvider>(), "cus_123", "in_1", 636, "CHF",
                "Tax (7.7%)", "key-1:tax-item", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task The_two_lines_add_up_to_exactly_what_is_charged()
    {
        // The guarantee that matters more than the presentation: an invoice owing something other
        // than the amount taken from the card is voided by the check further down, so a split that
        // does not close would abandon every taxed renewal.
        var amounts = new List<long>();

        _invoices
            .Setup(client => client.CreateInvoiceItemAsync(
                It.IsAny<PaymentProvider>(), "cus_123", "in_1", It.IsAny<long>(), "CHF",
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((PaymentProvider _, string _, string _, long amount, string _, string _,
                string _, CancellationToken _) => amounts.Add(amount))
            .ReturnsAsync(new StripeInvoiceCallResult(StripeInvoiceOutcome.Success, "ii_1"));

        await Gateway().ChargeAsync(Taxed(), "key-1", "corr-1", CancellationToken.None);

        amounts.Sum().Should().Be(8_900);
    }

    [Fact]
    public async Task An_untaxed_renewal_is_still_one_line()
    {
        await Gateway().ChargeAsync(Request(), "key-1", "corr-1", CancellationToken.None);

        _invoices.Verify(
            client => client.CreateInvoiceItemAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_split_that_does_not_add_up_to_the_charge_is_invoiced_as_one_line()
    {
        // A renewal partly paid from banked credit: net and tax describe the whole period, while the
        // charge is what was left to collect. Two lines would invoice more than was taken, and the
        // amount check would then void the invoice — so the split is dropped rather than the charge.
        var request = Taxed();
        request.AmountMinor = 5_000;

        await Gateway().ChargeAsync(request, "key-1", "corr-1", CancellationToken.None);

        _invoices.Verify(
            client => client.CreateInvoiceItemAsync(
                It.IsAny<PaymentProvider>(), "cus_123", "in_1", 5_000, "CHF",
                It.IsAny<string>(), "key-1:item", It.IsAny<CancellationToken>()),
            Times.Once);
        _invoices.Verify(
            client => client.CreateInvoiceItemAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), "key-1:tax-item",
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_tax_line_stripe_refuses_abandons_the_invoice_rather_than_undercharging()
    {
        // Finalizing with the subtotal alone would owe less than the renewal charges, which the
        // amount check voids anyway — after the customer has been shown a draft that was wrong.
        _invoices
            .Setup(client => client.CreateInvoiceItemAsync(
                It.IsAny<PaymentProvider>(), "cus_123", "in_1", 636, "CHF",
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceCallResult(
                StripeInvoiceOutcome.Rejected, SafeErrorCode: "invoice_item_invalid"));

        var result = await Gateway().ChargeAsync(
            Taxed(), "key-1", "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        // The provider's own code where it gave one, which is how every other rejected call on this
        // path reports: our fallback name is for a refusal that arrives without one.
        result.ErrorCode.Should().Be("invoice_item_invalid");
        _invoices.Verify(
            client => client.VoidInvoiceAsync(
                It.IsAny<PaymentProvider>(), "in_1", It.IsAny<CancellationToken>()),
            Times.Once);
        _invoices.Verify(
            client => client.FinalizeInvoiceAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>The same charge, split the way a 7.7%-exclusive price splits it.</summary>
    private static SubscriptionChargeRequest Taxed()
    {
        var request = Request();

        request.NetAmountMinor = 8_264;
        request.TaxAmountMinor = 636;
        request.TaxRateBasisPoints = 770;

        return request;
    }

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
