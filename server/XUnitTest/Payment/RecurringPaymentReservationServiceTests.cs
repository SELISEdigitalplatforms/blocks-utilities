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
}
