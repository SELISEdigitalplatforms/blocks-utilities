using Blocks.Genesis;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
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
    private readonly IPaymentRepository _payments;
    private readonly ICurrencyMinorUnitResolver _minorUnits;
    private readonly IPaymentWebhookStateTransitionService _webhookTransitions;
    private readonly ISubscriptionActivationProcessor _activationProcessor;
    private readonly ISubscriptionRenewalService _renewalService;
    private readonly ISubscriptionSimulatedOutcomeSource _scriptedOutcomes;

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
        IOptionsMonitor<PaymentOptions> paymentOptions,
        IPaymentRepository payments,
        ICurrencyMinorUnitResolver minorUnits,
        IPaymentWebhookStateTransitionService webhookTransitions,
        ISubscriptionActivationProcessor activationProcessor,
        ISubscriptionRenewalService renewalService,
        ISubscriptionSimulatedOutcomeSource scriptedOutcomes)
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
        _payments = payments;
        _minorUnits = minorUnits;
        _webhookTransitions = webhookTransitions;
        _activationProcessor = activationProcessor;
        _renewalService = renewalService;
        _scriptedOutcomes = scriptedOutcomes;
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

    public Task<SubscriptionOperationResult<SubscriptionSimulationActionResponse>> MarkPaymentSucceededAsync(
        string subscriptionId,
        MarkPaymentSucceededRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return MarkPaymentOutcomeAsync(
            subscriptionId,
            request.OrganizationId,
            request.PaymentPurpose,
            succeeded: true,
            request.ProviderReference,
            failureKind: null,
            errorCode: null,
            request.RunProcessor,
            "MarkPaymentSucceeded",
            correlationId,
            cancellationToken);
    }

    public Task<SubscriptionOperationResult<SubscriptionSimulationActionResponse>> MarkPaymentFailedAsync(
        string subscriptionId,
        MarkPaymentFailedRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return MarkPaymentOutcomeAsync(
            subscriptionId,
            request.OrganizationId,
            request.PaymentPurpose,
            succeeded: false,
            providerReference: null,
            request.FailureKind,
            request.ErrorCode,
            request.RunProcessor,
            "MarkPaymentFailed",
            correlationId,
            cancellationToken);
    }

    private async Task<SubscriptionOperationResult<SubscriptionSimulationActionResponse>> MarkPaymentOutcomeAsync(
        string subscriptionId,
        string? organizationId,
        SubscriptionPaymentPurpose purpose,
        bool succeeded,
        string? providerReference,
        SimulatedPaymentFailureKind? failureKind,
        string? errorCode,
        bool runProcessor,
        string action,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTime.UtcNow;

        var caller = BlocksContext.GetContext();

        if (!SubscriptionSimulationGuard.IsAuthorized(
                caller?.OrganizationId, _paymentOptions.CurrentValue, caller?.Permissions))
        {
            return SubscriptionOperationResult<SubscriptionSimulationActionResponse>.Failure(
                PaymentFailureKind.Unavailable,
                "subscription_simulation_forbidden",
                "This caller may not use the subscription simulation harness.",
                correlationId);
        }

        if (string.IsNullOrWhiteSpace(organizationId))
        {
            return SubscriptionOperationResult<SubscriptionSimulationActionResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_simulation_organization_required",
                "organizationId is required: the console has no subscription of its own.",
                correlationId,
                new Dictionary<string, string[]>
                {
                    ["OrganizationId"] = ["'Organization Id' must not be empty."]
                });
        }

        var resolution = await _contextResolver.ResolveAsync(
            correlationId, organizationId, cancellationToken);

        if (!resolution.IsSuccess || resolution.Context is null)
        {
            return resolution.ToFailure<SubscriptionSimulationActionResponse>(correlationId);
        }

        var context = resolution.Context;

        var subscription = await _subscriptions.GetAsync(
            context.TenantId, context.OrganizationId, subscriptionId, cancellationToken);

        if (subscription is null)
        {
            await RecordRunAsync(
                context, subscriptionId, action, correlationId,
                "Failed", "subscription_not_found", cancellationToken);

            return SubscriptionOperationResult<SubscriptionSimulationActionResponse>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "The subscription does not exist.",
                correlationId);
        }

        var before = Summarize(subscription);
        var simulationRunId = $"sim_{Guid.NewGuid():N}";

        (SubscriptionOperationResult<SubscriptionSimulationActionResponse>? Failure, string Note) outcome = purpose switch
        {
            SubscriptionPaymentPurpose.InitialCharge => await SettleInitialChargeAsync(
                context, subscription, succeeded, providerReference, failureKind, errorCode,
                runProcessor, simulationRunId, correlationId, cancellationToken),
            SubscriptionPaymentPurpose.Renewal => await SettleRenewalAsync(
                context, subscription, succeeded, failureKind, errorCode, runProcessor,
                correlationId, cancellationToken),
            _ => (SubscriptionOperationResult<SubscriptionSimulationActionResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_simulation_purpose_invalid",
                "Unsupported payment purpose.",
                correlationId), string.Empty)
        };

        if (outcome.Failure is { } failure)
        {
            await RecordRunAsync(
                context, subscriptionId, action, correlationId,
                "Failed", failure.ErrorCode, cancellationToken);

            return failure;
        }

        var refreshed = await _subscriptions.GetAsync(
            context.TenantId, context.OrganizationId, subscriptionId, cancellationToken) ?? subscription;
        var after = Summarize(refreshed);

        var stateResult = await GetStateAsync(
            subscriptionId, organizationId, 100, 100, true, correlationId, cancellationToken);

        await RecordRunAsync(
            context, subscriptionId, action, correlationId, "Succeeded", null, cancellationToken);

        return SubscriptionOperationResult<SubscriptionSimulationActionResponse>.Success(
            new SubscriptionSimulationActionResponse
            {
                SimulationRunId = simulationRunId,
                Action = action,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = DateTime.UtcNow,
                Before = before,
                After = after,
                State = stateResult.IsSuccess && stateResult.Value is not null
                    ? stateResult.Value
                    : new SubscriptionSimulationStateResponse(),
                CorrelationId = correlationId
            },
            correlationId);
    }

    /// <summary>
    /// Settles the first charge through the real webhook-equivalent write, then — unless
    /// <paramref name="runProcessor"/> says otherwise — the real activation processor for this
    /// one link only.
    /// </summary>
    /// <remarks>
    /// Deliberately never populates a card token or provider-customer reference on the simulated
    /// payload. <see cref="Outbox.SubscriptionActivationProcessor"/> reads a saved card back from
    /// the payment's own shopper reference afterward, and getting a fake token wrong there risks
    /// either an exception in code this harness does not own or a card that looks saved but
    /// silently is not — either is worse than the honest gap this leaves: a subscription
    /// activated this way has no simulated card, so a subsequently simulated renewal on it needs
    /// a payment method recorded some other way.
    /// </remarks>
    private async Task<(SubscriptionOperationResult<SubscriptionSimulationActionResponse>? Failure, string Note)>
        SettleInitialChargeAsync(
            SubscriptionContext context,
            SubscriptionDetail subscription,
            bool succeeded,
            string? providerReference,
            SimulatedPaymentFailureKind? failureKind,
            string? errorCode,
            bool runProcessor,
            string simulationRunId,
            string correlationId,
            CancellationToken cancellationToken)
    {
        if (subscription.Status != SubscriptionStatus.Incomplete)
        {
            return (Fail<SubscriptionSimulationActionResponse>(
                PaymentFailureKind.Conflict,
                "subscription_simulation_already_settled",
                "This subscription's first charge has already been settled.",
                correlationId), string.Empty);
        }

        var link = await _paymentLinks.FindBySubscriptionAsync(
            context.TenantId, subscription.ItemId, cancellationToken);

        if (link is null ||
            link.Purpose != SubscriptionPaymentPurpose.InitialCharge ||
            link.State != SubscriptionPaymentLinkState.Pending)
        {
            return (Fail<SubscriptionSimulationActionResponse>(
                PaymentFailureKind.NotFound,
                "subscription_simulation_no_pending_payment",
                "There is no pending initial-charge payment to settle.",
                correlationId), string.Empty);
        }

        if (!succeeded && failureKind is SimulatedPaymentFailureKind.ProviderUnavailable
                or SimulatedPaymentFailureKind.OutcomeUnknown)
        {
            // Honest to production: an unreachable or ambiguous provider answer never produces a
            // webhook at all, so nothing here should move the subscription forward either — the
            // real system would leave this exact charge pending until either a real webhook
            // eventually lands or RecoverStaleAsync's grace period expires.
            return (null,
                "No webhook was simulated: an unavailable or unknown provider outcome leaves " +
                "the charge exactly where a real one would — still pending.");
        }

        var payment = await _payments.GetByIdAsync(
            context.TenantId, link.PaymentDetailId, cancellationToken);

        if (payment is null)
        {
            return (Fail<SubscriptionSimulationActionResponse>(
                PaymentFailureKind.NotFound,
                "subscription_simulation_no_pending_payment",
                "The linked payment record could not be found.",
                correlationId), string.Empty);
        }

        if (!_minorUnits.TryConvert(payment.PreciseAmount, payment.CurrencyCode, out var amountMinor))
        {
            return (Fail<SubscriptionSimulationActionResponse>(
                PaymentFailureKind.Unavailable,
                "subscription_simulation_amount_conversion_failed",
                "The payment amount could not be converted to minor units.",
                correlationId), string.Empty);
        }

        var reference = providerReference ?? simulationRunId;

        var webhook = new PaymentWebhookInbox
        {
            TenantId = context.TenantId,
            ProviderName = payment.ProviderName,
            WebhookType = "Simulated",
            EventCode = succeeded ? "SIMULATED_AUTHORISATION" : "SIMULATED_REFUSAL",
            Intent = WebhookIntent.Authorization,
            PspReference = reference,
            EventDateUtc = DateTime.UtcNow,
            CorrelationId = correlationId,
            NormalizedPayload = new PaymentWebhookPayload
            {
                PaymentDetailId = payment.ItemId,
                PspReference = reference,
                Success = succeeded,
                FundsCaptured = succeeded ? true : null,
                AmountMinorUnits = amountMinor,
                CurrencyCode = payment.CurrencyCode,
                ProviderFailureCode = succeeded ? null : errorCode ?? DefaultErrorCode(failureKind),
                ProviderFailureSummary = succeeded ? null : DefaultErrorMessage(failureKind)
            }
        };

        try
        {
            await _webhookTransitions.ApplyAsync(webhook, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return (Fail<SubscriptionSimulationActionResponse>(
                PaymentFailureKind.Unavailable,
                "subscription_simulation_settlement_failed",
                exception.Message,
                correlationId), string.Empty);
        }

        if (!runProcessor)
        {
            return (null, "The simulated payment outcome was recorded; activation was not run.");
        }

        await _activationProcessor.SettleLinkAsync(link, cancellationToken);

        return (null, succeeded
            ? "The subscription activated (or started its trial)."
            : "The subscription was expired after the simulated decline.");
    }

    /// <summary>
    /// Scripts the one gateway call a renewal makes, then runs the real renewal service — there
    /// is no separate pending state for a renewal charge to resolve, since production charges it
    /// synchronously in the same call that decides the outcome.
    /// </summary>
    private async Task<(SubscriptionOperationResult<SubscriptionSimulationActionResponse>? Failure, string Note)>
        SettleRenewalAsync(
            SubscriptionContext context,
            SubscriptionDetail subscription,
            bool succeeded,
            SimulatedPaymentFailureKind? failureKind,
            string? errorCode,
            bool runProcessor,
            string correlationId,
            CancellationToken cancellationToken)
    {
        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue))
        {
            return (Fail<SubscriptionSimulationActionResponse>(
                PaymentFailureKind.Conflict,
                "subscription_simulation_not_renewable",
                "Only an Active or PastDue subscription can be renewed.",
                correlationId), string.Empty);
        }

        if (subscription.SettlementReservation is not null)
        {
            return (Fail<SubscriptionSimulationActionResponse>(
                PaymentFailureKind.Conflict,
                "subscription_simulation_settlement_in_flight",
                "A quantity or plan change is still settling; renewal is deferred until it resolves.",
                correlationId), string.Empty);
        }

        if (!runProcessor)
        {
            return (null,
                "runProcessor=false: a renewal has no separate settlement fact to record " +
                "without actually running it, so nothing was charged.");
        }

        _scriptedOutcomes.ScriptNext(new ScriptedChargeOutcome(
            succeeded ? SimulatedChargeOutcome.Succeeded : MapFailureOutcome(failureKind),
            succeeded ? null : errorCode ?? DefaultErrorCode(failureKind),
            succeeded ? null : DefaultErrorMessage(failureKind)));

        await _renewalService.RenewAsync(subscription, cancellationToken);

        return (null, succeeded
            ? "The renewal charge succeeded and the period advanced."
            : "The renewal charge failed; dunning was applied.");
    }

    private static SimulatedChargeOutcome MapFailureOutcome(SimulatedPaymentFailureKind? kind) => kind switch
    {
        SimulatedPaymentFailureKind.ProviderUnavailable => SimulatedChargeOutcome.Unavailable,
        SimulatedPaymentFailureKind.OutcomeUnknown => SimulatedChargeOutcome.TimedOut,
        _ => SimulatedChargeOutcome.Rejected
    };

    private static string DefaultErrorCode(SimulatedPaymentFailureKind? kind) => kind switch
    {
        SimulatedPaymentFailureKind.Declined => "card_declined",
        SimulatedPaymentFailureKind.InsufficientFunds => "insufficient_funds",
        SimulatedPaymentFailureKind.PaymentMethodExpired => "expired_card",
        SimulatedPaymentFailureKind.ProviderUnavailable => "provider_unavailable",
        SimulatedPaymentFailureKind.OutcomeUnknown => "outcome_unknown",
        _ => "payment_failed"
    };

    private static string DefaultErrorMessage(SimulatedPaymentFailureKind? kind) => kind switch
    {
        SimulatedPaymentFailureKind.Declined => "Simulated: the card was declined.",
        SimulatedPaymentFailureKind.InsufficientFunds => "Simulated: insufficient funds.",
        SimulatedPaymentFailureKind.PaymentMethodExpired => "Simulated: the payment method has expired.",
        SimulatedPaymentFailureKind.ProviderUnavailable => "Simulated: the payment provider was unreachable.",
        SimulatedPaymentFailureKind.OutcomeUnknown => "Simulated: no answer arrived from the provider.",
        _ => "Simulated payment failure."
    };

    private static SubscriptionSimulationSummary Summarize(SubscriptionDetail subscription) => new()
    {
        SubscriptionStatus = subscription.Status.ToString(),
        CurrentPeriodEndUtc = subscription.CurrentPeriodEndUtc,
        NextFeeBillingAtUtc = subscription.NextFeeBillingAtUtc,
        DunningAttemptCount = subscription.DunningAttemptCount,
        LastRenewalPaymentDetailId = subscription.LastRenewalPaymentDetailId,
        Version = subscription.Version
    };

    private static SubscriptionOperationResult<T> Fail<T>(
        PaymentFailureKind kind, string code, string message, string correlationId) =>
        SubscriptionOperationResult<T>.Failure(kind, code, message, correlationId);

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
