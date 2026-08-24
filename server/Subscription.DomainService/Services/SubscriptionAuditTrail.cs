using Microsoft.Extensions.Logging;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;

namespace Subscription.DomainService.Services;

public sealed class SubscriptionAuditTrail : ISubscriptionAuditTrail
{
    private readonly ISubscriptionAuditRepository _repository;
    private readonly ILogger<SubscriptionAuditTrail> _logger;

    public SubscriptionAuditTrail(
        ISubscriptionAuditRepository repository,
        ILogger<SubscriptionAuditTrail> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task RecordAsync(SubscriptionAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        auditEvent.OperationId = string.IsNullOrWhiteSpace(auditEvent.OperationId)
            ? auditEvent.CorrelationId
            : auditEvent.OperationId;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationId"] = auditEvent.OperationId,
            ["CorrelationId"] = auditEvent.CorrelationId,
            ["TenantHash"] = PaymentLogValue.Hash(auditEvent.TenantId),
            ["OrganizationHash"] = PaymentLogValue.Hash(auditEvent.OrganizationId),
            ["SubscriptionHash"] = PaymentLogValue.Hash(auditEvent.SubscriptionId),
            ["SubscriptionOperation"] = auditEvent.Operation,
            ["SubscriptionStage"] = auditEvent.Stage,
            ["Outcome"] = auditEvent.Outcome
        });

        _logger.LogInformation(
            "Subscription lifecycle event Source={Source} AmountMinor={AmountMinor} Currency={Currency} ErrorCode={ErrorCode} Attempt={Attempt}",
            PaymentLogValue.Label(auditEvent.Source), auditEvent.AmountMinor,
            PaymentLogValue.Label(auditEvent.CurrencyCode),
            PaymentLogValue.Label(auditEvent.ErrorCode), auditEvent.Attempt);

        try
        {
            await _repository.AppendAsync(auditEvent, cancellationToken);
        }
        catch (Exception exception)
        {
            // Never invite a duplicate charge by failing a completed money operation because its
            // secondary audit write failed. The critical log is monitored and reconciliation can
            // reconstruct the state from payment and subscription ledgers.
            _logger.LogCritical(exception, "SUBSCRIPTION_AUDIT_WRITE_FAILED");
        }
    }
}
