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
    private readonly IPaymentOrganizationResolver _organizationResolver;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly TimeProvider _time;

    public PaymentReservationService(
        IPaymentRepository repository,
        IPaymentIdempotencyCache idempotencyCache,
        IPaymentResponseMapper responseMapper,
        IPaymentOrganizationResolver organizationResolver,
        IOptionsMonitor<PaymentOptions> options,
        TimeProvider? time = null)
    {
        _repository = repository;
        _idempotencyCache = idempotencyCache;
        _responseMapper = responseMapper;
        _organizationResolver = organizationResolver;
        _options = options;
        _time = time ?? TimeProvider.System;
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
        var leaseUntil = _time.GetUtcNow().UtcDateTime.AddSeconds(
            Math.Clamp(_options.CurrentValue.ProcessingLeaseSeconds, 10, 120));
        // Which organization the payment belongs to decides which merchant account takes the
        // money, because provider lookup keys off the payment's organization rather than the
        // caller's context.
        //
        // When the caller already froze an exact provider row (ExpectedProviderId), OrganizationId
        // is not a naming request to authorize -- it is the scope readiness already resolved and
        // validated at subscription creation, reproduced verbatim, null included. Routing it
        // through the general-purpose organization-naming resolver conflates two different
        // questions: PaymentOrganizationResolver.ResolveAsync answers "may this caller name an
        // organization" and, when nothing was named, substitutes the ambient caller's own
        // organization -- correct for an ordinary caller, but wrong here, because a genuinely
        // tenant-wide frozen scope (OrganizationId == null) is not "nothing was named"; it is an
        // explicit fact that must survive unchanged. For a console caller acting on behalf of
        // another organization, substituting the console's own ambient organization for that null
        // can resolve a *different* PaymentProvider row than the one readiness validated, which is
        // exactly the divergence subscription_payment_provider_scope_mismatch exists to catch. See
        // ExpectedProviderId's own remarks and BillingAccount.ProviderOrganizationId.
        var organization = request.ExpectedProviderId is { Length: > 0 }
            ? new PaymentOrganizationResolution(request.OrganizationId, null)
            : await _organizationResolver.ResolveAsync(
                request.OrganizationId,
                context,
                correlationId,
                cancellationToken);

        if (organization.Failure != null)
        {
            return Terminal(organization.Failure);
        }

        var payment = CreatePayment(
            request,
            context,
            organization.OrganizationId,
            organization.RequestNamedTheOrganization
                ? PaymentOrigins.BlocksConsole
                : PaymentOrigins.Api,
            idempotencyKey,
            correlationId,
            requestHash,
            leaseId,
            leaseUntil,
            _time.GetUtcNow().UtcDateTime);

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
        string? organizationId,
        string origin,
        string idempotencyKey,
        string correlationId,
        string requestHash,
        string leaseId,
        DateTime leaseUntil,
        DateTime nowUtc) => new()
        {
            TenantId = context.TenantId,
            ProviderName = request.ProviderName.ToUpperInvariant(),
            PaymentStatus = PaymentStatuses.Initiating,
            Amount = (double)request.Amount,
            PreciseAmount = request.Amount,
            CurrencyCode = request.CurrencyCode.ToUpperInvariant(),
            RememberCard = request.ShouldSavePaymentMethod,
            IsRecurring = request.IsRecurring,
            OrganizationId = organizationId,
            Origin = origin,
            UserId = context.UserId,
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
            CreatedAtUtc = nowUtc,
            LastUpdatedDateUtc = nowUtc,
            PaymentDate = nowUtc
        };

    private static PaymentReservationResult Terminal(PaymentOperationResult result) => new(null, null, result);

    private static PaymentOperationResult Conflict(string code, string message, string correlationId) =>
        PaymentOperationResult.Failure(PaymentFailureKind.Conflict, code, message, correlationId);
}
