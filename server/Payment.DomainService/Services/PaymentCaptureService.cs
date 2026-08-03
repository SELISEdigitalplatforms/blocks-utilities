using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentCaptureService : IPaymentCaptureService
{
    private readonly IPaymentExecutionContextResolver _contextResolver;
    private readonly IPaymentCapturePreflightService _preflight;
    private readonly IPaymentDistributedLock _distributedLock;
    private readonly IPaymentCaptureReservationService _reservations;
    private readonly IPaymentCaptureInitiationService _initiation;
    private readonly IPaymentCaptureRepository _captures;
    private readonly IPaymentCaptureResponseMapper _responses;

    public PaymentCaptureService(
        IPaymentExecutionContextResolver contextResolver,
        IPaymentCapturePreflightService preflight,
        IPaymentDistributedLock distributedLock,
        IPaymentCaptureReservationService reservations,
        IPaymentCaptureInitiationService initiation,
        IPaymentCaptureRepository captures,
        IPaymentCaptureResponseMapper responses)
    {
        _contextResolver = contextResolver;
        _preflight = preflight;
        _distributedLock = distributedLock;
        _reservations = reservations;
        _initiation = initiation;
        _captures = captures;
        _responses = responses;
    }

    public async Task<PaymentCaptureOperationResult>
        CreatePaymentCaptureAsync(
            string paymentDetailId,
            CreatePaymentCaptureRequest request,
            string idempotencyKey,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var contextResolution = _contextResolver.Resolve(correlationId);

        if (!contextResolution.IsSuccess)
        {
            var failure = contextResolution.Failure!;

            return PaymentCaptureOperationResult.Failure(
                failure.FailureKind,
                failure.ErrorCode,
                failure.ErrorMessage,
                correlationId,
                failure.ValidationErrors);
        }

        var context = contextResolution.Context!;
        var preflight = await _preflight.ExecuteAsync(
            paymentDetailId,
            request,
            idempotencyKey,
            context,
            correlationId,
            cancellationToken);

        if (!preflight.IsSuccess)
        {
            return preflight.Failure!;
        }

        await using var coordinationLock =
            await _distributedLock.TryAcquireAsync(
                PaymentHashing.CreateLockResource(
                    context.TenantId,
                    $"capture:{paymentDetailId}"),
                cancellationToken);

        var reservation = await _reservations.ReserveAsync(
            preflight.Payment!,
            preflight.Provider!,
            request,
            idempotencyKey,
            correlationId,
            cancellationToken);

        if (!reservation.CanSubmit)
        {
            return reservation.TerminalResult! with
            {
                RateLimit = preflight.RateLimit
            };
        }

        var result = await _initiation.SubmitAsync(
            reservation.Payment!,
            reservation.Capture!,
            preflight.Provider!,
            reservation.LeaseId!,
            preflight.MinorUnits,
            correlationId,
            cancellationToken);

        return result with { RateLimit = preflight.RateLimit };
    }

    public async Task<PaymentCaptureOperationResult>
        GetPaymentCaptureAsync(
            string paymentDetailId,
            string captureId,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var contextResolution = _contextResolver.Resolve(correlationId);

        if (!contextResolution.IsSuccess)
        {
            var failure = contextResolution.Failure!;

            return PaymentCaptureOperationResult.Failure(
                failure.FailureKind,
                failure.ErrorCode,
                failure.ErrorMessage,
                correlationId);
        }

        var payment = await _captures.GetPaymentByCaptureIdAsync(
            contextResolution.Context!.TenantId,
            captureId,
            cancellationToken);
        var capture = payment?.ItemId == paymentDetailId
            ? payment.Captures.FirstOrDefault(
                item => item.CaptureId == captureId)
            : null;

        return payment == null || capture == null
            ? PaymentCaptureOperationResult.Failure(
                PaymentFailureKind.NotFound,
                "payment_capture_not_found",
                "The payment capture was not found.",
                correlationId)
            : PaymentCaptureOperationResult.Success(
                _responses.Map(payment.ItemId, capture),
                correlationId);
    }
}
