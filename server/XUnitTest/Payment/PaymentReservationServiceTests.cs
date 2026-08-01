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

public sealed class PaymentReservationServiceTests
{
    private readonly Mock<IPaymentRepository> _repository = new();
    private readonly Mock<IPaymentIdempotencyCache> _idempotencyCache = new();
    private readonly Mock<IPaymentResponseMapper> _responseMapper = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _options = new();
    private readonly PaymentExecutionContext _context = new("tenant", "actor", "org");
    private readonly MakePaymentRequest _request = new()
    {
        ProviderName = "provider",
        Amount = 10,
        CurrencyCode = "eur",
        OrderId = "order-1"
    };
    private readonly string _idempotencyKey = Guid.NewGuid().ToString();

    public PaymentReservationServiceTests()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions());
    }

    private PaymentReservationService CreateService() => new(
        _repository.Object, _idempotencyCache.Object, _responseMapper.Object, _options.Object);

    private Task<PaymentReservationResult> RunAsync() =>
        CreateService().ReserveAsync(_request, _context, _idempotencyKey, "corr", CancellationToken.None);

    private string ExpectedHash() => PaymentHashing.CreateRequestHash(_request);

    private void SetupCreate(bool ok) =>
        _repository.Setup(r => r.TryCreateAsync(It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>())).ReturnsAsync(ok);

    private PaymentDetail Existing(string status) => new()
    {
        ItemId = "existing-1",
        TenantId = "tenant",
        RequestHash = ExpectedHash(),
        PaymentStatus = status
    };

    private void SetupExistingViaKey(PaymentDetail? existing)
    {
        _idempotencyCache.Setup(c => c.GetPaymentIdAsync("tenant", _idempotencyKey, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        _repository.Setup(r => r.GetByIdempotencyKeyAsync("tenant", _idempotencyKey, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
    }

    [Fact]
    public async Task ReserveAsync_CreateSucceeds_ReturnsInitiableAndCaches()
    {
        SetupCreate(true);

        var result = await RunAsync();

        result.CanInitiate.Should().BeTrue();
        _idempotencyCache.Verify(c => c.SetPaymentIdAsync("tenant", _idempotencyKey, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReserveAsync_CreateFailsNoExisting_ReturnsConflict()
    {
        SetupCreate(false);
        SetupExistingViaKey(null);

        var result = await RunAsync();

        result.TerminalResult!.ErrorCode.Should().Be("payment_conflict");
    }

    [Fact]
    public async Task ReserveAsync_ExistingFoundViaCache_HashMismatch_ReturnsReuse()
    {
        SetupCreate(false);
        var existing = Existing(PaymentStatuses.Initiating);
        existing.RequestHash = "different";
        _idempotencyCache.Setup(c => c.GetPaymentIdAsync("tenant", _idempotencyKey, It.IsAny<CancellationToken>())).ReturnsAsync("existing-1");
        _repository.Setup(r => r.GetByIdAsync("tenant", "existing-1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await RunAsync();

        result.TerminalResult!.ErrorCode.Should().Be("idempotency_key_reused");
    }

    [Theory]
    [InlineData(PaymentStatuses.Processing)]
    [InlineData(PaymentStatuses.Authorized)]
    [InlineData(PaymentStatuses.Refused)]
    public async Task ReserveAsync_ExistingProcessed_ReturnsReplaySuccess(string status)
    {
        SetupCreate(false);
        var existing = Existing(status);
        SetupExistingViaKey(existing);
        _responseMapper.Setup(m => m.Map(existing)).Returns(new PaymentResponse());

        var result = await RunAsync();

        result.TerminalResult!.IsReplay.Should().BeTrue();
    }

    [Fact]
    public async Task ReserveAsync_ExistingFailed_ReturnsProviderRejected()
    {
        SetupCreate(false);
        var existing = Existing(PaymentStatuses.MakePaymentFailed);
        existing.FailureCode = "declined";
        SetupExistingViaKey(existing);

        var result = await RunAsync();

        result.TerminalResult!.FailureKind.Should().Be(PaymentFailureKind.ProviderRejected);
        result.TerminalResult.ErrorCode.Should().Be("declined");
    }

    [Fact]
    public async Task ReserveAsync_ExistingClaimFails_ReturnsInProgress()
    {
        SetupCreate(false);
        SetupExistingViaKey(Existing(PaymentStatuses.Initiating));
        _repository.Setup(r => r.TryClaimInitiationAsync("tenant", "existing-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetail?)null);

        var result = await RunAsync();

        result.TerminalResult!.ErrorCode.Should().Be("payment_in_progress");
    }

    [Fact]
    public async Task ReserveAsync_ExistingClaimSucceeds_ReturnsInitiable()
    {
        SetupCreate(false);
        SetupExistingViaKey(Existing(PaymentStatuses.Initiating));
        var claimed = new PaymentDetail { ItemId = "existing-1" };
        _repository.Setup(r => r.TryClaimInitiationAsync("tenant", "existing-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimed);

        var result = await RunAsync();

        result.CanInitiate.Should().BeTrue();
        result.Payment.Should().BeSameAs(claimed);
    }
}
