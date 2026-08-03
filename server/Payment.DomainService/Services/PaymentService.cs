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

        var context = contextResolution.Context!;
        var payment = await _repository.GetByIdAsync(
            context.TenantId,
            paymentId,
            cancellationToken);

        // Filtering the list alone would be theatre: anyone holding an identifier could still
        // read the payment directly. Reported as not found rather than forbidden, so the
        // response cannot be used to confirm that an identifier exists in another organization.
        if (payment != null && !IsVisibleTo(payment, context))
        {
            payment = null;
        }

        return payment == null
            ? PaymentOperationResult.Failure(
                PaymentFailureKind.NotFound,
                "payment_not_found",
                "The payment was not found.",
                correlationId)
            : PaymentOperationResult.Success(_responseMapper.Map(payment), correlationId);
    }

    /// <summary>
    /// Whether a payment is within the caller's organization scope.
    /// </summary>
    /// <remarks>
    /// The same rule the listing filter applies: an organization sees its own payments and the
    /// ones made before organizations existed, which belong to none and are the tenant's shared
    /// history. A caller with no organization is not scoped and sees the whole tenant.
    /// </remarks>
    private static bool IsVisibleTo(
        PaymentDetail payment,
        PaymentExecutionContext context) =>
        string.IsNullOrWhiteSpace(context.OrganizationId) ||
        string.IsNullOrWhiteSpace(payment.OrganizationId) ||
        string.Equals(
            payment.OrganizationId,
            context.OrganizationId,
            StringComparison.Ordinal);

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
