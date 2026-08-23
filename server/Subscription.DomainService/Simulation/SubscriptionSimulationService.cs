using Blocks.Genesis;
using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;

namespace Subscription.DomainService.Simulation;

public sealed class SubscriptionSimulationService : ISubscriptionSimulationService
{
    private const int MaximumLimit = 500;

    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionResponseMapper _responseMapper;
    private readonly IEntitlementService _entitlements;
    private readonly ISubscriptionInvoiceHistoryRepository _invoiceHistory;
    private readonly ISubscriptionUsageInvoiceRepository _usageInvoices;
    private readonly ISubscriptionPaymentLinkRepository _paymentLinks;
    private readonly ISubscriptionAuditRepository _auditEvents;
    private readonly ISubscriptionSimulationRunRepository _simulationRuns;
    private readonly IOptionsMonitor<PaymentOptions> _paymentOptions;

    public SubscriptionSimulationService(
        ISubscriptionContextResolver contextResolver,
        ISubscriptionRepository subscriptions,
        ISubscriptionResponseMapper responseMapper,
        IEntitlementService entitlements,
        ISubscriptionInvoiceHistoryRepository invoiceHistory,
        ISubscriptionUsageInvoiceRepository usageInvoices,
        ISubscriptionPaymentLinkRepository paymentLinks,
        ISubscriptionAuditRepository auditEvents,
        ISubscriptionSimulationRunRepository simulationRuns,
        IOptionsMonitor<PaymentOptions> paymentOptions)
    {
        _contextResolver = contextResolver;
        _subscriptions = subscriptions;
        _responseMapper = responseMapper;
        _entitlements = entitlements;
        _invoiceHistory = invoiceHistory;
        _usageInvoices = usageInvoices;
        _paymentLinks = paymentLinks;
        _auditEvents = auditEvents;
        _simulationRuns = simulationRuns;
        _paymentOptions = paymentOptions;
    }

    public async Task<SubscriptionOperationResult<SubscriptionSimulationStateResponse>> GetStateAsync(
        string subscriptionId,
        string? organizationId,
        int auditLimit,
        int paymentLimit,
        bool includeBackgroundWork,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Checked against the caller's own token, never against anything a request names — that
        // is exactly the question this decides the trustworthiness of.
        var caller = BlocksContext.GetContext();

        if (!SubscriptionSimulationGuard.IsAuthorized(
                caller?.OrganizationId, _paymentOptions.CurrentValue, caller?.Permissions))
        {
            return SubscriptionOperationResult<SubscriptionSimulationStateResponse>.Failure(
                PaymentFailureKind.Unavailable,
                "subscription_simulation_forbidden",
                "This caller may not use the subscription simulation harness.",
                correlationId);
        }

        if (string.IsNullOrWhiteSpace(organizationId))
        {
            return SubscriptionOperationResult<SubscriptionSimulationStateResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_simulation_organization_required",
                "organizationId is required: the console has no subscription of its own.",
                correlationId,
                new Dictionary<string, string[]>
                {
                    ["OrganizationId"] = ["'Organization Id' must not be empty."]
                });
        }

        if (!IsValidLimit(auditLimit) || !IsValidLimit(paymentLimit))
        {
            return SubscriptionOperationResult<SubscriptionSimulationStateResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_simulation_limit_invalid",
                $"auditLimit and paymentLimit must be between 1 and {MaximumLimit}.",
                correlationId,
                new Dictionary<string, string[]>
                {
                    ["AuditLimit"] = [$"Must be between 1 and {MaximumLimit}."],
                    ["PaymentLimit"] = [$"Must be between 1 and {MaximumLimit}."]
                });
        }

        // Resolves and, for the console, verifies the named organization actually exists — the
        // guard above only established who is asking, not that what they asked for is real.
        var resolution = await _contextResolver.ResolveAsync(
            correlationId, organizationId, cancellationToken);

        if (!resolution.IsSuccess || resolution.Context is null)
        {
            return resolution.ToFailure<SubscriptionSimulationStateResponse>(correlationId);
        }

        var context = resolution.Context;

        // Scoped by organization here, the same as every ordinary read — a subscription outside
        // it reports as missing rather than forbidden, so its existence in another organization
        // is never confirmed.
        var subscription = await _subscriptions.GetAsync(
            context.TenantId, context.OrganizationId, subscriptionId, cancellationToken);

        if (subscription is null)
        {
            await RecordRunAsync(
                context, subscriptionId, "InspectState", correlationId,
                "Failed", "subscription_not_found", cancellationToken);

            return SubscriptionOperationResult<SubscriptionSimulationStateResponse>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "The subscription does not exist.",
                correlationId);
        }

        var entitlementsResult = await _entitlements.GetAsync(
            fresh: true, organizationId: context.OrganizationId, correlationId, cancellationToken);

        var payments = await _invoiceHistory.ListBySubscriptionAsync(
            context.TenantId, context.OrganizationId, subscriptionId, paymentLimit, cancellationToken);

        var usageInvoices = await _usageInvoices.ListBySubscriptionAsync(
            context.TenantId, subscriptionId, paymentLimit, cancellationToken);

        var pendingCheckout = await _paymentLinks.FindBySubscriptionAsync(
            context.TenantId, subscriptionId, cancellationToken);

        var auditEvents = await _auditEvents.ListAsync(
            context.TenantId, context.OrganizationId, subscriptionId, auditLimit, cancellationToken);

        var response = new SubscriptionSimulationStateResponse
        {
            SubscriptionId = subscriptionId,
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            // Never given a checkout URL: this is a diagnostic read, not a way to drive an actual
            // checkout, and a live URL is one of the values the audit rules already exclude.
            Subscription = _responseMapper.ToResponse(subscription, checkoutUrl: null),
            Entitlements = entitlementsResult.IsSuccess ? entitlementsResult.Value : null,
            SettlementReservation = MapSettlementReservation(subscription.SettlementReservation),
            PendingCheckout = MapPendingCheckout(pendingCheckout),
            Payments = payments.Select(MapPayment).ToList(),
            UsageInvoices = usageInvoices.Select(MapUsageInvoice).ToList(),
            BackgroundWork = includeBackgroundWork ? MapBackgroundWork(subscription) : null,
            AuditEvents = auditEvents.Select(MapAuditEvent).ToList(),
            CorrelationId = correlationId
        };

        await RecordRunAsync(
            context, subscriptionId, "InspectState", correlationId,
            "Succeeded", null, cancellationToken);

        return SubscriptionOperationResult<SubscriptionSimulationStateResponse>.Success(
            response, correlationId);
    }

    private static bool IsValidLimit(int limit) => limit is > 0 and <= MaximumLimit;

    private async Task RecordRunAsync(
        SubscriptionContext context,
        string subscriptionId,
        string action,
        string correlationId,
        string outcome,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        // Fire-and-observe, like the business audit trail: a simulation run failing to record
        // itself must never take down the read it is only describing.
        try
        {
            await _simulationRuns.AppendAsync(
                new SubscriptionSimulationRun
                {
                    TenantId = context.TenantId,
                    OrganizationId = context.OrganizationId,
                    SubscriptionId = subscriptionId,
                    ActorId = context.ActorId,
                    Action = action,
                    CorrelationId = correlationId,
                    Outcome = outcome,
                    ErrorCode = errorCode,
                    CompletedAtUtc = DateTime.UtcNow
                },
                cancellationToken);
        }
        catch
        {
            // Deliberately swallowed — see the remark above.
        }
    }

    private static SimulationSettlementReservationResponse? MapSettlementReservation(
        SettlementReservation? reservation) =>
        reservation is null
            ? null
            : new SimulationSettlementReservationResponse
            {
                ReservationId = reservation.ReservationId,
                Kind = reservation.Kind.ToString(),
                ChargeAmountMinor = reservation.ChargeAmountMinor,
                ReservedAtUtc = reservation.ReservedAtUtc,
                ReservedAtVersion = reservation.ReservedAtVersion,
                CorrelationId = reservation.CorrelationId
            };

    private static SimulationPendingCheckoutResponse? MapPendingCheckout(
        SubscriptionPaymentLink? link) =>
        link is null
            ? null
            : new SimulationPendingCheckoutResponse
            {
                PaymentDetailId = link.PaymentDetailId,
                Purpose = link.Purpose.ToString(),
                State = link.State.ToString(),
                AttemptCount = link.AttemptCount,
                NextCheckAtUtc = link.NextCheckAtUtc,
                LastError = link.LastError
            };

    private static SimulationPaymentResponse MapPayment(
        SubscriptionInvoiceHistoryRecord record) =>
        new()
        {
            PaymentDetailId = record.PaymentDetailId,
            ProviderName = record.ProviderName,
            OrderId = record.OrderId,
            Description = record.Description,
            Amount = record.Amount,
            RefundedAmount = record.RefundedAmount,
            CurrencyCode = record.CurrencyCode,
            Status = record.Status,
            IssuedAtUtc = record.IssuedAtUtc
        };

    private static SimulationUsageInvoiceResponse MapUsageInvoice(
        SubscriptionUsageInvoice invoice) =>
        new()
        {
            UsageInvoiceId = invoice.ItemId,
            PeriodKey = invoice.PeriodKey,
            CurrencyCode = invoice.CurrencyCode,
            TotalAmountMinor = invoice.TotalAmountMinor,
            TaxAmountMinor = invoice.TaxAmountMinor,
            State = invoice.State.ToString(),
            AttemptCount = invoice.AttemptCount,
            NextAttemptAtUtc = invoice.NextAttemptAtUtc,
            PaymentDetailId = invoice.PaymentDetailId,
            LastError = invoice.LastError
        };

    private static SimulationBackgroundWorkResponse MapBackgroundWork(
        SubscriptionDetail subscription)
    {
        var events = subscription.OutboxEvents;

        return new SimulationBackgroundWorkResponse
        {
            PendingCount = events.Count(e => e.Status == SubscriptionOutboxStatus.Pending),
            ProcessingCount = events.Count(e => e.Status == SubscriptionOutboxStatus.Processing),
            RetryScheduledCount = events.Count(e => e.Status == SubscriptionOutboxStatus.RetryScheduled),
            PublishedCount = events.Count(e => e.Status == SubscriptionOutboxStatus.Published),
            AbandonedCount = events.Count(e => e.Status == SubscriptionOutboxStatus.Abandoned),
            Items = events
                .OrderByDescending(e => e.CreatedAtUtc)
                .Select(e => new SimulationBackgroundWorkItemResponse
                {
                    EventId = e.EventId,
                    EventType = e.EventType,
                    Status = e.Status.ToString(),
                    AttemptCount = e.AttemptCount,
                    NextAttemptAtUtc = e.NextAttemptAtUtc,
                    LeaseExpiresAtUtc = e.LeaseExpiresAtUtc,
                    LastError = e.LastError,
                    CorrelationId = e.CorrelationId,
                    CreatedAtUtc = e.CreatedAtUtc
                })
                .ToList()
        };
    }

    private static SubscriptionAuditEventResponse MapAuditEvent(SubscriptionAuditEvent auditEvent) =>
        new()
        {
            EventId = auditEvent.ItemId,
            OperationId = auditEvent.OperationId,
            CorrelationId = auditEvent.CorrelationId,
            Operation = auditEvent.Operation,
            Stage = auditEvent.Stage,
            Outcome = auditEvent.Outcome,
            Source = auditEvent.Source,
            AmountMinor = auditEvent.AmountMinor,
            CurrencyCode = auditEvent.CurrencyCode,
            FromStatus = auditEvent.FromStatus,
            ToStatus = auditEvent.ToStatus,
            ErrorCode = auditEvent.ErrorCode,
            FailureKind = auditEvent.FailureKind,
            Attempt = auditEvent.Attempt,
            OccurredAtUtc = auditEvent.OccurredAtUtc
        };
}
