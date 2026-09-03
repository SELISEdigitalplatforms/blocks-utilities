using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class RecurringPaymentReservationServiceTests
{
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IPaymentResponseMapper> _responseMapper = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _options = new();
    private readonly PaymentExecutionContext _context = new("tenant", "actor", "org");
    private readonly CreateRecurringPaymentRequest _request = new()
    {
        ProviderName = "provider",
        StoredPaymentMethodId = "method-1",
        Amount = 10,
        CurrencyCode = "eur",
        OrderId = "order-1"
    };
    private readonly string _idempotencyKey = Guid.NewGuid().ToString();

    public RecurringPaymentReservationServiceTests()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions());
    }

    private RecurringPaymentReservationService CreateService() => new(
        _payments.Object, _responseMapper.Object, _options.Object);

    private Task<PaymentReservationResult> RunAsync() =>
        CreateService().ReserveAsync(_request, _context, "shopper-ref", _idempotencyKey, "corr", CancellationToken.None);

    private string ExpectedHash() => PaymentHashing.CreateRequestHash(_request);

    private void SetupCreate(bool ok) =>
        _payments.Setup(p => p.TryCreateAsync(It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>())).ReturnsAsync(ok);

    private void SetupExisting(PaymentDetail? existing) =>
        _payments.Setup(p => p.GetByIdempotencyKeyAsync("tenant", _idempotencyKey, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

    private PaymentDetail Existing(string status) => new()
    {
        ItemId = "existing-1",
        TenantId = "tenant",
        RequestHash = ExpectedHash(),
        PaymentFlow = PaymentFlows.RecurringCharge,
        PaymentStatus = status
    };

    [Fact]
    public async Task ReserveAsync_CreateSucceeds_ReturnsInitiable()
    {
        SetupCreate(true);

        var result = await RunAsync();

        result.CanInitiate.Should().BeTrue();
        result.Payment.Should().NotBeNull();
        result.LeaseId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ReserveAsync_CreateFailsNoExistingButOrderUsed_ReturnsOrderConflict()
    {
        SetupCreate(false);
        SetupExisting(null);
        _payments.Setup(p => p.GetRecurringPaymentByOrderIdAsync("tenant", "order-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail());

        var result = await RunAsync();

        result.TerminalResult!.ErrorCode.Should().Be("recurring_payment_order_already_used");
    }

    [Fact]
    public async Task ReserveAsync_CreateFailsNoExistingNoOrder_ReturnsConflict()
    {
        SetupCreate(false);
        SetupExisting(null);
        _payments.Setup(p => p.GetRecurringPaymentByOrderIdAsync("tenant", "order-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetail?)null);

        var result = await RunAsync();

        result.TerminalResult!.ErrorCode.Should().Be("payment_conflict");
    }

    [Fact]
    public async Task ReserveAsync_CreateFailsHashMismatch_ReturnsIdempotencyReuse()
    {
        SetupCreate(false);
        var existing = Existing(PaymentStatuses.Initiating);
        existing.RequestHash = "different";
        SetupExisting(existing);

        var result = await RunAsync();

        result.TerminalResult!.ErrorCode.Should().Be("idempotency_key_reused");
    }

    [Fact]
    public async Task ReserveAsync_CreateFailsFlowMismatch_ReturnsIdempotencyReuse()
    {
        SetupCreate(false);
        var existing = Existing(PaymentStatuses.Initiating);
        existing.PaymentFlow = PaymentFlows.HostedCheckout;
        SetupExisting(existing);

        var result = await RunAsync();

        result.TerminalResult!.ErrorCode.Should().Be("idempotency_key_reused");
    }

    [Theory]
    [InlineData(PaymentStatuses.Processing)]
    [InlineData(PaymentStatuses.Authorized)]
    [InlineData(PaymentStatuses.Refused)]
    public async Task ReserveAsync_CreateFailsExistingProcessed_ReturnsReplaySuccess(string status)
    {
        SetupCreate(false);
        var existing = Existing(status);
        SetupExisting(existing);
        _responseMapper.Setup(m => m.Map(existing)).Returns(new PaymentResponse());

        var result = await RunAsync();

        result.TerminalResult!.IsSuccess.Should().BeTrue();
        result.TerminalResult.IsReplay.Should().BeTrue();
    }

    [Fact]
    public async Task ReserveAsync_CreateFailsExistingFailed_ReturnsProviderRejected()
    {
        SetupCreate(false);
        var existing = Existing(PaymentStatuses.MakePaymentFailed);
        existing.FailureCode = "declined";
        SetupExisting(existing);

        var result = await RunAsync();

        result.TerminalResult!.FailureKind.Should().Be(PaymentFailureKind.ProviderRejected);
        result.TerminalResult.ErrorCode.Should().Be("declined");
    }

    [Fact]
    public async Task ReserveAsync_CreateFailsExistingClaimFails_ReturnsInProgress()
    {
        SetupCreate(false);
        SetupExisting(Existing(PaymentStatuses.Initiating));
        _payments.Setup(p => p.TryClaimInitiationAsync("tenant", "existing-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetail?)null);

        var result = await RunAsync();

        result.TerminalResult!.ErrorCode.Should().Be("payment_in_progress");
    }

    [Fact]
    public async Task ReserveAsync_CreateFailsExistingClaimSucceeds_ReturnsInitiable()
    {
        SetupCreate(false);
        SetupExisting(Existing(PaymentStatuses.Initiating));
        var claimed = new PaymentDetail { ItemId = "existing-1" };
        _payments.Setup(p => p.TryClaimInitiationAsync("tenant", "existing-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimed);

        var result = await RunAsync();

        result.CanInitiate.Should().BeTrue();
        result.Payment.Should().BeSameAs(claimed);
    }

    /// <summary>
    /// A subscription renewal, dunning retry, settlement or usage invoice charged through this
    /// provider-neutral path -- Adyen included -- carries its full invoice breakdown along with
    /// it, exactly as a Stripe Invoice charge records on its own <see cref="PaymentDetail"/>.
    /// </summary>
    /// <remarks>
    /// Closes a real gap: <c>CreateRecurringPaymentRequest.SubscriptionInvoiceBreakdown</c> used
    /// to be forwarded from <c>RecurringChargeBillingGateway</c> with nowhere to land -- this
    /// class's own <see cref="RecurringPaymentReservationService.CreatePayment"/> never read it,
    /// so an Adyen-routed charge recorded a payment with none of the figures its invoice needed.
    /// </remarks>
    [Fact]
    public async Task ReserveAsync_WithSubscriptionBreakdown_RecordsItOnTheCreatedPayment()
    {
        PaymentDetail? recorded = null;
        _payments
            .Setup(p => p.TryCreateAsync(It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()))
            .Callback((PaymentDetail payment, CancellationToken _) => recorded = payment)
            .ReturnsAsync(true);

        _request.ProviderName = "ADYEN-ONLINE";
        _request.SubscriptionInvoiceBreakdown = new SubscriptionInvoiceBreakdown
        {
            NetAmountMinor = 83_640,
            TaxAmountMinor = 6_360,
            TaxRateBasisPoints = 770,
            TaxMode = "Exclusive",
            CreditConsumedMinor = 1_500,
            GrossAmountMinor = 100_000,
            BuiltInDiscountMinor = 8_000,
            PromotionalDiscountMinor = 9_200,
            AutomaticDiscountBasisPoints = 800,
            QuantityDiscountBasisPoints = 500,
            DiscountCombination = "Additive"
        };

        await RunAsync();

        recorded.Should().NotBeNull();
        recorded!.ProviderName.Should().Be("ADYEN-ONLINE");
        recorded.SubscriptionNetAmountMinor.Should().Be(83_640);
        recorded.SubscriptionTaxAmountMinor.Should().Be(6_360);
        recorded.SubscriptionTaxRateBasisPoints.Should().Be(770);
        recorded.SubscriptionTaxMode.Should().Be("Exclusive");
        recorded.SubscriptionCreditAmountMinor.Should().Be(1_500);
        recorded.SubscriptionGrossAmountMinor.Should().Be(100_000);
        recorded.SubscriptionBuiltInDiscountMinor.Should().Be(8_000);
        recorded.SubscriptionPromotionalDiscountMinor.Should().Be(9_200);
        recorded.SubscriptionAutomaticDiscountBasisPoints.Should().Be(800);
        recorded.SubscriptionQuantityDiscountBasisPoints.Should().Be(500);
        recorded.SubscriptionDiscountCombination.Should().Be("Additive");
    }

    [Fact]
    public async Task ReserveAsync_WithNoSubscriptionBreakdown_RecordsNoneRatherThanZeroes()
    {
        // An ordinary unscheduled-card-on-file charge never sets this -- the created payment
        // should read as "not a subscription charge", not as "a subscription charge with nothing
        // in it".
        PaymentDetail? recorded = null;
        _payments
            .Setup(p => p.TryCreateAsync(It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()))
            .Callback((PaymentDetail payment, CancellationToken _) => recorded = payment)
            .ReturnsAsync(true);

        await RunAsync();

        recorded.Should().NotBeNull();
        recorded!.SubscriptionGrossAmountMinor.Should().BeNull();
        recorded.SubscriptionNetAmountMinor.Should().BeNull();
        recorded.SubscriptionSettlement.Should().BeNull();
    }
}
