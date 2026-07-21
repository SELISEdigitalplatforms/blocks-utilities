using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class RecurringPaymentReservationService :
    IRecurringPaymentReservationService
{
    private readonly IPaymentRepository _payments;
    private readonly IPaymentResponseMapper _responseMapper;
    private readonly IOptionsMonitor<PaymentOptions> _options;

    public RecurringPaymentReservationService(
        IPaymentRepository payments,
        IPaymentResponseMapper responseMapper,
        IOptionsMonitor<PaymentOptions> options)
    {
        _payments = payments;
        _responseMapper = responseMapper;
        _options = options;
    }

    public async Task<PaymentReservationResult> ReserveAsync(
        CreateRecurringPaymentRequest request,
        PaymentExecutionContext context,
        string shopperReference,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var requestHash =
            PaymentHashing.CreateRequestHash(request);
        var leaseId = Guid.NewGuid().ToString("N");
        var leaseUntilUtc = DateTime.UtcNow.AddSeconds(
            Math.Clamp(
                _options.CurrentValue.ProcessingLeaseSeconds,
                10,
                120));
        var payment = CreatePayment(
            request,
            context,
            shopperReference,
            idempotencyKey,
            correlationId,
            requestHash,
            leaseId,
            leaseUntilUtc);

        if (await _payments.TryCreateAsync(
                payment,
                cancellationToken))
        {
            return new PaymentReservationResult(
                payment,
                leaseId,
                null);
        }

        var existing =
            await _payments.GetByIdempotencyKeyAsync(
                context.TenantId,
                idempotencyKey,
                cancellationToken);

        if (existing == null)
        {
            var orderPayment =
                await _payments
                    .GetRecurringPaymentByOrderIdAsync(
                        context.TenantId,
                        request.OrderId,
                        cancellationToken);

            if (orderPayment != null)
            {
                return Terminal(Conflict(
                    "recurring_payment_order_already_used",
                    "A recurring payment already exists for this order.",
                    correlationId));
            }

            return Terminal(Conflict(
                "payment_conflict",
                "The payment could not be reserved.",
                correlationId));
        }

        if (!PaymentHashing.RequestHashesMatch(
                existing.RequestHash,
                requestHash) ||
            !string.Equals(
                existing.PaymentFlow,
                PaymentFlows.RecurringCharge,
                StringComparison.Ordinal))
        {
            return Terminal(Conflict(
                "idempotency_key_reused",
                "The idempotency key was already used with a different request.",
                correlationId));
        }

        if (existing.PaymentStatus is
            PaymentStatuses.Processing or
            PaymentStatuses.Authorized or
            PaymentStatuses.Refused)
        {
            return Terminal(PaymentOperationResult.Success(
                _responseMapper.Map(existing),
                correlationId,
                replay: true));
        }

        if (existing.PaymentStatus ==
            PaymentStatuses.MakePaymentFailed)
        {
            return Terminal(PaymentOperationResult.Failure(
                PaymentFailureKind.ProviderRejected,
                existing.FailureCode ??
                "recurring_payment_failed",
                "The previous recurring payment attempt failed.",
                correlationId));
        }

        var claimed =
            await _payments.TryClaimInitiationAsync(
                context.TenantId,
                existing.ItemId,
                leaseId,
                leaseUntilUtc,
                cancellationToken);

        return claimed == null
            ? Terminal(Conflict(
                "payment_in_progress",
                "The payment is already being processed.",
                correlationId))
            : new PaymentReservationResult(
                claimed,
                leaseId,
                null);
    }

    private static PaymentDetail CreatePayment(
        CreateRecurringPaymentRequest request,
        PaymentExecutionContext context,
        string shopperReference,
        string idempotencyKey,
        string correlationId,
        string requestHash,
        string leaseId,
        DateTime leaseUntilUtc) =>
        new()
        {
            TenantId = context.TenantId,
            ProviderName =
                request.ProviderName.ToUpperInvariant(),
            PaymentStatus = PaymentStatuses.Initiating,
            PaymentFlow = PaymentFlows.RecurringCharge,
            Amount = (double)request.Amount,
            PreciseAmount = request.Amount,
            CurrencyCode =
                request.CurrencyCode.ToUpperInvariant(),
            IsRecurring = true,
            RecurringProcessingModel =
                NormalizeModel(
                    request.RecurringProcessingModel),
            StoredPaymentMethodPublicId =
                request.StoredPaymentMethodId,
            ShopperReference = shopperReference,
            OrganizationId = context.OrganizationId,
            OrderId = request.OrderId,
            Description = request.Description?.Trim(),
            TransactionId = Guid.NewGuid().ToString(),
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            CorrelationId = correlationId,
            ProcessingLeaseId = leaseId,
            ProcessingLeaseExpiresAtUtc =
                leaseUntilUtc,
            InitiationAttemptCount = 1,
            CreatedAtUtc = DateTime.UtcNow,
            LastUpdatedDateUtc = DateTime.UtcNow,
            PaymentDate = DateTime.UtcNow
        };

    private static string NormalizeModel(string model) =>
        model.Equals(
            "UnscheduledCardOnFile",
            StringComparison.OrdinalIgnoreCase)
            ? "UnscheduledCardOnFile"
            : "Subscription";

    private static PaymentReservationResult Terminal(
        PaymentOperationResult result) =>
        new(null, null, result);

    private static PaymentOperationResult Conflict(
        string code,
        string message,
        string correlationId) =>
        PaymentOperationResult.Failure(
            PaymentFailureKind.Conflict,
            code,
            message,
            correlationId);
}
