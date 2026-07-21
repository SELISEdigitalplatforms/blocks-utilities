using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentService : IPaymentService
{
    private readonly IPaymentExecutionContextResolver _contextResolver;
    private readonly IPaymentPreflightService _preflightService;
    private readonly IPaymentDistributedLock _distributedLock;
    private readonly IPaymentReservationService _reservationService;
    private readonly IPaymentInitiationService _initiationService;
    private readonly IPaymentRepository _repository;
    private readonly IPaymentResponseMapper _responseMapper;
    private readonly IRecurringPaymentInitiationService
        _recurringPaymentInitiation;

    public PaymentService(
        IPaymentExecutionContextResolver contextResolver,
        IPaymentPreflightService preflightService,
        IPaymentDistributedLock distributedLock,
        IPaymentReservationService reservationService,
        IPaymentInitiationService initiationService,
        IPaymentRepository repository,
        IPaymentResponseMapper responseMapper,
        IRecurringPaymentInitiationService
            recurringPaymentInitiation)
    {
        _contextResolver = contextResolver;
        _preflightService = preflightService;
        _distributedLock = distributedLock;
        _reservationService = reservationService;
        _initiationService = initiationService;
        _repository = repository;
        _responseMapper = responseMapper;
        _recurringPaymentInitiation =
            recurringPaymentInitiation;
    }

    public async Task<PaymentOperationResult> MakePaymentAsync(
        MakePaymentRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var contextResolution = _contextResolver.Resolve(correlationId);
        if (!contextResolution.IsSuccess) return contextResolution.Failure!;

        var context = contextResolution.Context!;
        var preflight = await _preflightService.ExecuteAsync(
            request,
            idempotencyKey,
            context,
            correlationId,
            cancellationToken);
        if (!preflight.IsSuccess) return preflight.Failure!;

        var lockResource = PaymentHashing.CreateLockResource(context.TenantId, idempotencyKey);
        await using var coordinationLock = await _distributedLock.TryAcquireAsync(
            lockResource,
            cancellationToken);

        var reservation = await _reservationService.ReserveAsync(
            request,
            context,
            idempotencyKey,
            correlationId,
            cancellationToken);
        if (!reservation.CanInitiate)
        {
            return reservation.TerminalResult!.WithRateLimit(preflight.RateLimit!);
        }

        var result = await _initiationService.InitiateAsync(
            request,
            context,
            reservation.Payment!,
            reservation.LeaseId!,
            preflight.MinorUnits,
            correlationId,
            cancellationToken);
        return result.WithRateLimit(preflight.RateLimit!);
    }

    public async Task<PaymentOperationResult> GetPaymentAsync(
        string paymentId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var contextResolution = _contextResolver.Resolve(correlationId);
        if (!contextResolution.IsSuccess) return contextResolution.Failure!;

        var payment = await _repository.GetByIdAsync(
            contextResolution.Context!.TenantId,
            paymentId,
            cancellationToken);
        return payment == null
            ? PaymentOperationResult.Failure(
                PaymentFailureKind.NotFound,
                "payment_not_found",
                "The payment was not found.",
                correlationId)
            : PaymentOperationResult.Success(_responseMapper.Map(payment), correlationId);
    }

    public Task RecoverAsync(
        PaymentDetail payment,
        CancellationToken cancellationToken) =>
        payment.PaymentFlow == PaymentFlows.RecurringCharge
            ? _recurringPaymentInitiation.RecoverAsync(
                payment,
                cancellationToken)
            : _initiationService.RecoverAsync(
                payment,
                cancellationToken);
}
