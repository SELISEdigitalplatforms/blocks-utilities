using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// What an operator can do about work the scheduler gave up on.
/// </summary>
/// <remarks>
/// Before this, recovery was a database edit — the worst available option for financial work,
/// because it is unaudited and easy to get half right. Clearing a status without its lease leaves an
/// item nobody can claim; leaving attempts at their ceiling leaves one that dead-letters again on
/// its first failure. Both look like a requeue that quietly did nothing.
/// <para>
/// Requeueing does not re-decide whether the work is still due. The handler re-reads tenant state
/// and decides that, which is the right division: an operator is saying "try again", not "charge
/// this". A month-old renewal requeued today will find its subscription has moved on and complete
/// without billing anybody.
/// </para>
/// </remarks>
public sealed class SubscriptionWorkRecoveryService : ISubscriptionWorkRecoveryService
{
    private readonly ISubscriptionWorkQueue _queue;
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly ISubscriptionAuditTrail? _audit;
    private readonly ILogger<SubscriptionWorkRecoveryService> _logger;
    private readonly TimeProvider _time;

    public SubscriptionWorkRecoveryService(
        ISubscriptionWorkQueue queue,
        ISubscriptionContextResolver contextResolver,
        ILogger<SubscriptionWorkRecoveryService> logger,
        ISubscriptionAuditTrail? audit = null,
        TimeProvider? time = null)
    {
        _queue = queue;
        _contextResolver = contextResolver;
        _audit = audit;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionOperationResult<IReadOnlyList<DeadLetteredWorkResponse>>> ListAsync(
        int limit,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(correlationId, null, cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<IReadOnlyList<DeadLetteredWorkResponse>>(correlationId);
        }

        // Scoped to the caller's own tenant, always. The collection spans every tenant on the
        // platform, and a cross-tenant view is a different question with a different answer about
        // who may ask it — so it is not answered here by default.
        var dead = await _queue.ListDeadLetteredAsync(
            limit,
            cancellationToken,
            resolution.Context!.TenantId);

        var now = _time.GetUtcNow().UtcDateTime;

        return SubscriptionOperationResult<IReadOnlyList<DeadLetteredWorkResponse>>.Success(
            dead.Select(work => Describe(work, now)).ToList(),
            correlationId);
    }

    public Task<SubscriptionOperationResult<DeadLetteredWorkResponse>> RequeueAsync(
        string workItemId,
        string reason,
        string correlationId,
        CancellationToken cancellationToken) =>
        DecideAsync(
            workItemId,
            reason,
            correlationId,
            "Requeued",
            (item, why, token) => _queue.TryRequeueAsync(item, why, token),
            cancellationToken);

    public Task<SubscriptionOperationResult<DeadLetteredWorkResponse>> AbandonAsync(
        string workItemId,
        string reason,
        string correlationId,
        CancellationToken cancellationToken) =>
        DecideAsync(
            workItemId,
            reason,
            correlationId,
            "Abandoned",
            (item, why, token) => _queue.TryAbandonAsync(item, why, token),
            cancellationToken);

    /// <summary>
    /// The shape both decisions share: resolve who is asking, check they may, write once, audit.
    /// </summary>
    private async Task<SubscriptionOperationResult<DeadLetteredWorkResponse>> DecideAsync(
        string workItemId,
        string reason,
        string correlationId,
        string stage,
        Func<string, string, CancellationToken, Task<bool>> decide,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            // Required, not merely recorded. A dead letter set aside without a reason is a decision
            // nobody can review, and reviewing them is the only reason to keep them.
            return Failure<DeadLetteredWorkResponse>(
                PaymentFailureKind.Validation,
                "subscription_work_reason_required",
                "Say why: this decision is part of the audit record.",
                correlationId);
        }

        var resolution = await _contextResolver.ResolveAsync(correlationId, null, cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<DeadLetteredWorkResponse>(correlationId);
        }

        var context = resolution.Context!;
        var work = await _queue.GetAsync(workItemId, cancellationToken);

        if (work is null)
        {
            return Failure<DeadLetteredWorkResponse>(
                PaymentFailureKind.NotFound,
                "subscription_work_not_found",
                "There is no such work item.",
                correlationId);
        }

        // Checked before the write, and the reason it is checked at all: the item id is a platform
        // identifier, so without this any authenticated caller could act on another tenant's work
        // simply by naming it.
        if (!string.Equals(work.TenantId, context.TenantId, StringComparison.Ordinal))
        {
            return Failure<DeadLetteredWorkResponse>(
                PaymentFailureKind.NotFound,
                "subscription_work_not_found",
                "There is no such work item.",
                correlationId);
        }

        if (!await decide(workItemId, reason, cancellationToken))
        {
            // The status moved between the read and the write — another operator, or the item was
            // never dead-lettered to begin with.
            return Failure<DeadLetteredWorkResponse>(
                PaymentFailureKind.Conflict,
                "subscription_work_not_dead_lettered",
                "This work is not dead-lettered. Re-read it before deciding again.",
                correlationId);
        }

        await AuditAsync(work, context, stage, reason, cancellationToken);

        _logger.LogWarning(
            "Dead-lettered subscription work was {Stage} by an operator WorkItemId={WorkItemId} " +
            "WorkType={WorkType} TenantHash={TenantHash} ActorHash={ActorHash} " +
            "CorrelationId={CorrelationId}",
            stage,
            work.ItemId,
            work.WorkType,
            PaymentLogValue.Hash(work.TenantId),
            PaymentLogValue.Hash(context.ActorId),
            PaymentLogValue.Label(correlationId));

        var updated = await _queue.GetAsync(workItemId, cancellationToken) ?? work;

        return SubscriptionOperationResult<DeadLetteredWorkResponse>.Success(
            Describe(updated, _time.GetUtcNow().UtcDateTime),
            correlationId);
    }

    /// <summary>
    /// Records who decided what, and why.
    /// </summary>
    /// <remarks>
    /// The actor is the point. A log line says work was requeued; this says who requeued it and on
    /// what grounds, which is what anybody asking months later actually needs.
    /// </remarks>
    private async Task AuditAsync(
        SubscriptionBackgroundWork work,
        SubscriptionContext context,
        string stage,
        string reason,
        CancellationToken cancellationToken)
    {
        if (_audit is null)
        {
            return;
        }

        try
        {
            await _audit.RecordAsync(
                new SubscriptionAuditEvent
                {
                    TenantId = work.TenantId,
                    OrganizationId = work.OrganizationId ?? context.OrganizationId,
                    SubscriptionId = string.IsNullOrWhiteSpace(work.AggregateId)
                        ? null
                        : work.AggregateId,
                    OperationId = work.ItemId,
                    CorrelationId = work.CorrelationId,
                    Operation = $"BackgroundWork:{work.WorkType}",
                    Stage = stage,
                    Outcome = "Succeeded",
                    Source = "Operator",
                    ActorId = context.ActorId,
                    UserId = context.UserId,
                    ErrorCode = work.LastErrorCode,
                    Attempt = work.AttemptCount,
                    Reason = reason
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The decision is already written. Failing the call now would tell the operator their
            // action did not happen, which would be false and would invite them to repeat it.
            _logger.LogError(
                exception,
                "An operator's recovery decision could not be audited WorkItemId={WorkItemId}",
                work.ItemId);
        }
    }

    private static DeadLetteredWorkResponse Describe(
        SubscriptionBackgroundWork work,
        DateTime nowUtc) => new()
    {
        WorkItemId = work.ItemId,
        WorkType = work.WorkType.ToString(),
        Status = work.Status.ToString(),
        WorkKey = work.WorkKey,
        SubscriptionId = string.IsNullOrWhiteSpace(work.AggregateId) ? null : work.AggregateId,
        OrganizationId = work.OrganizationId,
        AttemptCount = work.AttemptCount,
        MaxAttempts = work.MaxAttempts,
        LastErrorCode = work.LastErrorCode,
        LastErrorMessage = work.LastErrorMessage,
        DueAtUtc = work.DueAtUtc,
        LastTriedAtUtc = work.UpdatedAtUtc,
        CorrelationId = work.CorrelationId,
        // Stated rather than left to be worked out from two timestamps. How old a dead letter is
        // decides what to do with it: requeueing a month-old renewal is rarely what anyone means.
        AgeSeconds = (long)Math.Max(0, (nowUtc - work.DueAtUtc).TotalSeconds)
    };

    private static SubscriptionOperationResult<T> Failure<T>(
        PaymentFailureKind kind,
        string code,
        string message,
        string correlationId) =>
        SubscriptionOperationResult<T>.Failure(kind, code, message, correlationId);
}
