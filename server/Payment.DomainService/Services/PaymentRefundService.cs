using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentRefundService :
    IPaymentRefundService
{
    private readonly IPaymentExecutionContextResolver
        _contextResolver;
    private readonly IPaymentRefundPreflightService _preflight;
    private readonly IPaymentDistributedLock _distributedLock;
    private readonly IPaymentRefundReservationService
        _reservations;
    private readonly IPaymentRefundInitiationService _initiation;
    private readonly IPaymentRefundRepository _refunds;
    private readonly IPaymentRefundResponseMapper _responses;
    private readonly IOptionsMonitor<PaymentOptions> _options;

    public PaymentRefundService(
        IPaymentExecutionContextResolver contextResolver,
        IPaymentRefundPreflightService preflight,
        IPaymentDistributedLock distributedLock,
        IPaymentRefundReservationService reservations,
        IPaymentRefundInitiationService initiation,
        IPaymentRefundRepository refunds,
        IPaymentRefundResponseMapper responses,
        IOptionsMonitor<PaymentOptions> options)
    {
        _contextResolver = contextResolver;
        _preflight = preflight;
        _distributedLock = distributedLock;
        _reservations = reservations;
        _initiation = initiation;
        _refunds = refunds;
        _responses = responses;
        _options = options;
    }

    public async Task<PaymentRefundOperationResult>
        CreatePaymentRefundAsync(
            string paymentDetailId,
            CreatePaymentRefundRequest request,
            string idempotencyKey,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var contextResolution =
            _contextResolver.Resolve(correlationId);

        if (!contextResolution.IsSuccess)
        {
            var failure = contextResolution.Failure!;

            return PaymentRefundOperationResult.Failure(
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
                    $"refund:{paymentDetailId}"),
                cancellationToken);

        var reservation = await _reservations.ReserveAsync(
            preflight.Payment!,
            preflight.Provider!,
            request,
            preflight.ProviderOperation,
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
            reservation.Refund!,
            preflight.Provider!,
            reservation.LeaseId!,
            preflight.MinorUnits,
            correlationId,
            cancellationToken);

        return result with
        {
            RateLimit = preflight.RateLimit
        };
    }

    public async Task<PaymentRefundOperationResult>
        GetPaymentRefundAsync(
            string paymentDetailId,
            string refundId,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var contextResolution =
            _contextResolver.Resolve(correlationId);

        if (!contextResolution.IsSuccess)
        {
            var failure = contextResolution.Failure!;

            return PaymentRefundOperationResult.Failure(
                failure.FailureKind,
                failure.ErrorCode,
                failure.ErrorMessage,
                correlationId);
        }

        var context = contextResolution.Context!;
        var payment =
            await _refunds.GetPaymentByRefundIdAsync(
                context.TenantId,
                refundId,
                cancellationToken);
        var refund = payment?.ItemId == paymentDetailId &&
            PaymentOrganizationScope.MayAccess(
                payment,
                context.OrganizationId,
                _options.CurrentValue)
            ? payment.Refunds.FirstOrDefault(
                item => item.RefundId == refundId)
            : null;

        return payment == null || refund == null
            ? NotFound(correlationId)
            : PaymentRefundOperationResult.Success(
                _responses.Map(payment.ItemId, refund),
                correlationId);
    }

    public async Task<(
        IReadOnlyList<PaymentRefundResponse>? Refunds,
        PaymentRefundOperationResult? Failure)>
        GetPaymentRefundsAsync(
            string paymentDetailId,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var contextResolution =
            _contextResolver.Resolve(correlationId);

        if (!contextResolution.IsSuccess)
        {
            var failure = contextResolution.Failure!;

            return (
                null,
                PaymentRefundOperationResult.Failure(
                    failure.FailureKind,
                    failure.ErrorCode,
                    failure.ErrorMessage,
                    correlationId));
        }

        var context = contextResolution.Context!;
        var payment = await _refunds.GetPaymentAsync(
            context.TenantId,
            paymentDetailId,
            cancellationToken);

        if (payment == null ||
            !PaymentOrganizationScope.MayAccess(
                payment,
                context.OrganizationId,
                _options.CurrentValue))
        {
            return (null, NotFound(correlationId));
        }

        var responses = payment.Refunds
            .OrderByDescending(refund => refund.CreatedAtUtc)
            .Select(refund => _responses.Map(
                payment.ItemId,
                refund))
            .ToArray();

        return (responses, null);
    }

    private static PaymentRefundOperationResult NotFound(
        string correlationId) =>
        PaymentRefundOperationResult.Failure(
            PaymentFailureKind.NotFound,
            "payment_refund_not_found",
            "The payment refund was not found.",
            correlationId);
}
