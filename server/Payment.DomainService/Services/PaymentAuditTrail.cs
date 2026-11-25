using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentAuditTrail : IPaymentAuditTrail
{
    private readonly IPaymentAuditRepository _repository;
    private readonly IPaymentExecutionContextResolver _context;
    private readonly IOptions<PaymentOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<PaymentAuditTrail> _logger;

    public PaymentAuditTrail(
        IPaymentAuditRepository repository,
        IPaymentExecutionContextResolver context,
        IOptions<PaymentOptions> options,
        ILogger<PaymentAuditTrail> logger,
        TimeProvider? time = null)
    {
        _repository = repository;
        _context = context;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task RecordAsync(PaymentAuditEntry entry, string correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            if (_context.Resolve(correlationId).Context is not { } context)
            {
                // No tenant to file it under. Logged rather than dropped silently: an
                // unresolvable caller was refused by the action too, so nothing changed.
                _logger.LogWarning(
                    "Payment audit skipped because the caller could not be resolved " +
                    "Operation={Operation} CorrelationId={CorrelationId}",
                    entry.Operation,
                    PaymentLogValue.Id(correlationId));
                return;
            }

            await _repository.AppendAsync(
                new PaymentAuditEvent
                {
                    TenantId = context.TenantId,
                    OrganizationId = entry.OrganizationId ?? context.OrganizationId,
                    SubjectType = entry.SubjectType,
                    SubjectId = entry.SubjectId,
                    Operation = entry.Operation,
                    Outcome = entry.Outcome,
                    ErrorCode = entry.ErrorCode,
                    CorrelationId = correlationId,
                    ActorId = context.ActorId,
                    UserId = context.UserId,
                    Amount = entry.Amount,
                    CurrencyCode = entry.CurrencyCode,
                    Detail = entry.Detail,
                    OccurredAtUtc = _time.GetUtcNow().UtcDateTime
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogCritical(
                exception,
                "PAYMENT_AUDIT_WRITE_FAILED Operation={Operation} CorrelationId={CorrelationId}",
                entry.Operation,
                PaymentLogValue.Id(correlationId));
        }
    }

    public async Task<IReadOnlyList<PaymentAuditEvent>?> ListAsync(
        string? subjectType,
        string? subjectId,
        int limit,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (_context.Resolve(correlationId).Context is not { } context)
        {
            return null;
        }

        // The console and a tenant-wide integration (no organization on its token) see the whole
        // tenant; everyone else only their own organization.
        var organizationId = string.IsNullOrWhiteSpace(context.OrganizationId) ||
            PaymentOrganizationScope.RequestMayNameOrganization(context.OrganizationId, _options.Value)
                ? null
                : context.OrganizationId;

        return await _repository.ListAsync(
            context.TenantId,
            organizationId,
            subjectType,
            subjectId,
            limit <= 0 ? 100 : limit,
            cancellationToken);
    }
}
