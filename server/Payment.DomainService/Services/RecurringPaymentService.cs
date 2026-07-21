using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class RecurringPaymentService :
    IRecurringPaymentService
{
    private readonly IPaymentExecutionContextResolver
        _contextResolver;
    private readonly IRecurringPaymentPreflightService
        _preflight;
    private readonly IPaymentDistributedLock _distributedLock;
    private readonly IRecurringPaymentReservationService
        _reservations;
    private readonly IRecurringPaymentInitiationService
        _initiation;

    public RecurringPaymentService(
        IPaymentExecutionContextResolver contextResolver,
        IRecurringPaymentPreflightService preflight,
        IPaymentDistributedLock distributedLock,
        IRecurringPaymentReservationService reservations,
        IRecurringPaymentInitiationService initiation)
    {
        _contextResolver = contextResolver;
        _preflight = preflight;
        _distributedLock = distributedLock;
        _reservations = reservations;
        _initiation = initiation;
    }

    public async Task<PaymentOperationResult>
        CreateRecurringPaymentAsync(
            CreateRecurringPaymentRequest request,
            string idempotencyKey,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var contextResolution =
            _contextResolver.Resolve(correlationId);

        if (!contextResolution.IsSuccess)
        {
            return contextResolution.Failure!;
        }

        var context = contextResolution.Context!;
        var preflight = await _preflight.ExecuteAsync(
            request,
            idempotencyKey,
            context,
            correlationId,
            cancellationToken);

        if (!preflight.IsSuccess)
        {
            return preflight.RateLimit == null
                ? preflight.Failure!
                : preflight.Failure!.WithRateLimit(
                    preflight.RateLimit);
        }

        await using var idempotencyLock =
            await _distributedLock.TryAcquireAsync(
                PaymentHashing.CreateLockResource(
                    context.TenantId,
                    idempotencyKey),
                cancellationToken);

        var reservation = await _reservations.ReserveAsync(
            request,
            context,
            preflight.ShopperReference!,
            idempotencyKey,
            correlationId,
            cancellationToken);

        if (!reservation.CanInitiate)
        {
            return reservation.TerminalResult!.WithRateLimit(
                preflight.RateLimit!);
        }

        await using var paymentMethodLock =
            await _distributedLock.TryAcquireAsync(
                PaymentHashing.CreateLockResource(
                    context.TenantId,
                    $"stored-method:{request.StoredPaymentMethodId}"),
                cancellationToken);

        var result = await _initiation.InitiateAsync(
            reservation.Payment!,
            preflight.StoredPaymentMethod!,
            preflight.Provider!,
            reservation.LeaseId!,
            preflight.MinorUnits,
            correlationId,
            cancellationToken);

        return result.WithRateLimit(preflight.RateLimit!);
    }
}
