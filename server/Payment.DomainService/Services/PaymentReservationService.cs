using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentReservationService : IPaymentReservationService
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentIdempotencyCache _idempotencyCache;
    private readonly IPaymentResponseMapper _responseMapper;
    private readonly IOptionsMonitor<PaymentOptions> _options;

    public PaymentReservationService(
        IPaymentRepository repository,
        IPaymentIdempotencyCache idempotencyCache,
        IPaymentResponseMapper responseMapper,
        IOptionsMonitor<PaymentOptions> options)
    {
        _repository = repository;
        _idempotencyCache = idempotencyCache;
        _responseMapper = responseMapper;
        _options = options;
    }

    public async Task<PaymentReservationResult> ReserveAsync(
        MakePaymentRequest request,
        PaymentExecutionContext context,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var requestHash = PaymentHashing.CreateRequestHash(request);
        var leaseId = Guid.NewGuid().ToString("N");
        var leaseUntil = DateTime.UtcNow.AddSeconds(
            Math.Clamp(_options.CurrentValue.ProcessingLeaseSeconds, 10, 120));
        var payment = CreatePayment(
            request,
            context,
            idempotencyKey,
            correlationId,
            requestHash,
            leaseId,
            leaseUntil);

        if (await _repository.TryCreateAsync(payment, cancellationToken))
        {
            await CachePaymentIdAsync(payment, cancellationToken);
            return new PaymentReservationResult(payment, leaseId, null);
        }

        var existing = await FindExistingAsync(context.TenantId, idempotencyKey, cancellationToken);
        if (existing == null)
        {
            return Terminal(Conflict(
                "payment_conflict",
                "The payment could not be reserved.",
                correlationId));
        }

        await CachePaymentIdAsync(existing, cancellationToken);

        if (!PaymentHashing.RequestHashesMatch(existing.RequestHash, requestHash))
        {
            return Terminal(Conflict(
                "idempotency_key_reused",
                "The idempotency key was already used with a different request.",
                correlationId));
        }

        if (existing.PaymentStatus is PaymentStatuses.Processing or PaymentStatuses.Authorized or PaymentStatuses.Refused)
        {
            return Terminal(PaymentOperationResult.Success(
                _responseMapper.Map(existing),
                correlationId,
                replay: true));
        }

        if (existing.PaymentStatus == PaymentStatuses.MakePaymentFailed)
        {
            return Terminal(PaymentOperationResult.Failure(
                PaymentFailureKind.ProviderRejected,
                existing.FailureCode ?? "payment_failed",
                "The previous payment attempt failed.",
                correlationId));
        }

        var claimed = await _repository.TryClaimInitiationAsync(
            context.TenantId,
            existing.ItemId,
            leaseId,
            leaseUntil,
            cancellationToken);

        return claimed == null
            ? Terminal(Conflict(
                "payment_in_progress",
                "The payment is already being processed.",
                correlationId))
            : new PaymentReservationResult(claimed, leaseId, null);
    }

    private async Task<PaymentDetail?> FindExistingAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var cachedPaymentId = await _idempotencyCache.GetPaymentIdAsync(
            tenantId,
            idempotencyKey,
            cancellationToken);

        var payment = string.IsNullOrWhiteSpace(cachedPaymentId)
            ? null
            : await _repository.GetByIdAsync(tenantId, cachedPaymentId, cancellationToken);

        return payment ?? await _repository.GetByIdempotencyKeyAsync(
            tenantId,
            idempotencyKey,
            cancellationToken);
    }

    private Task CachePaymentIdAsync(PaymentDetail payment, CancellationToken cancellationToken) =>
        _idempotencyCache.SetPaymentIdAsync(
            payment.TenantId,
            payment.IdempotencyKey,
            payment.ItemId,
            cancellationToken);

    private static PaymentDetail CreatePayment(
        MakePaymentRequest request,
        PaymentExecutionContext context,
        string idempotencyKey,
        string correlationId,
        string requestHash,
        string leaseId,
        DateTime leaseUntil) => new()
        {
            TenantId = context.TenantId,
            ProviderName = request.ProviderName.ToUpperInvariant(),
            PaymentStatus = PaymentStatuses.Initiating,
            Amount = (double)request.Amount,
            PreciseAmount = request.Amount,
            CurrencyCode = request.CurrencyCode.ToUpperInvariant(),
            RememberCard = request.ShouldSavePaymentMethod,
            IsRecurring = request.IsRecurring,
            OrganizationId = context.OrganizationId,
            CustomerOrganizationId = request.CustomerOrganizationId,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,
            CustomerPhoneNumber = request.CustomerPhone,
            OrderId = request.OrderId,
            TransactionId = request.TransactionId ?? Guid.NewGuid().ToString(),
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            CorrelationId = correlationId,
            ProcessingLeaseId = leaseId,
            ProcessingLeaseExpiresAtUtc = leaseUntil,
            InitiationAttemptCount = 1,
            CreatedAtUtc = DateTime.UtcNow,
            LastUpdatedDateUtc = DateTime.UtcNow,
            PaymentDate = DateTime.UtcNow
        };

    private static PaymentReservationResult Terminal(PaymentOperationResult result) => new(null, null, result);

    private static PaymentOperationResult Conflict(string code, string message, string correlationId) =>
        PaymentOperationResult.Failure(PaymentFailureKind.Conflict, code, message, correlationId);
}
