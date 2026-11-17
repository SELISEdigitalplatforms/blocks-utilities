using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Subscription.Credits;

/// <summary>
/// Behaves like the Mongo grant ledger for the conditional writes callers rely on: a take that
/// would overdraw, or touch an expired or closed grant, changes nothing.
/// </summary>
internal sealed class InMemoryCreditGrants : ISubscriptionCreditGrantRepository
{
    public List<SubscriptionCreditGrant> All { get; } = [];

    /// <summary>How often a caller asked for live grants, so a test can prove it never did.</summary>
    public int LiveReads { get; private set; }

    /// <summary>Runs after every successful take, so a test can act at exactly that moment.</summary>
    public Action? AfterConsume { get; set; }

    /// <summary>Thrown by the next insert, as a store that is down would.</summary>
    public Exception? InsertFailure { get; set; }

    public Task<bool> TryInsertAsync(SubscriptionCreditGrant grant, CancellationToken cancellationToken)
    {
        // As the driver does: a write asked for under a cancelled token never happens.
        cancellationToken.ThrowIfCancellationRequested();

        if (InsertFailure is { } failure)
        {
            InsertFailure = null;
            throw failure;
        }

        if (All.Exists(existing => existing.ItemId == grant.ItemId))
        {
            return Task.FromResult(false);
        }

        All.Add(grant);
        return Task.FromResult(true);
    }

    public Task<SubscriptionCreditGrant?> GetAsync(string tenantId, string grantId, CancellationToken cancellationToken) =>
        Task.FromResult(All.Find(grant => grant.ItemId == grantId));

    // ---- the organization's outbox (spec 005) -------------------------------------------------

    public Task<bool> TryAppendEventAsync(
        string tenantId, string grantId, SubscriptionOutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        var grant = All.Find(candidate => candidate.TenantId == tenantId && candidate.ItemId == grantId);

        if (grant is null || grant.OutboxEvents.Exists(existing => existing.DeduplicationKey == outboxEvent.DeduplicationKey))
        {
            return Task.FromResult(false);
        }

        grant.OutboxEvents.Add(outboxEvent);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<SubscriptionCreditGrant>> ListWithDueEventsAsync(
        string tenantId, DateTime dueAtUtc, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionCreditGrant>>(
        [
            .. All.Where(grant => grant.TenantId == tenantId && grant.OutboxEvents.Exists(outboxEvent =>
                outboxEvent.Status is not SubscriptionOutboxStatus.Published and not SubscriptionOutboxStatus.Abandoned &&
                (outboxEvent.NextAttemptAtUtc is null || outboxEvent.NextAttemptAtUtc <= dueAtUtc))).Take(limit)
        ]);

    public Task<SubscriptionOutboxEvent?> TryClaimEventAsync(
        string tenantId, string grantId, string eventId, string leaseId, DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var outboxEvent = All.Find(grant => grant.TenantId == tenantId && grant.ItemId == grantId)?
            .OutboxEvents.Find(candidate =>
                candidate.EventId == eventId &&
                candidate.Status is not SubscriptionOutboxStatus.Published and not SubscriptionOutboxStatus.Abandoned &&
                (candidate.LeaseExpiresAtUtc is null || candidate.LeaseExpiresAtUtc <= DateTime.UtcNow));

        if (outboxEvent is null)
        {
            return Task.FromResult<SubscriptionOutboxEvent?>(null);
        }

        outboxEvent.Status = SubscriptionOutboxStatus.Processing;
        outboxEvent.LeaseId = leaseId;
        outboxEvent.LeaseExpiresAtUtc = leaseExpiresAtUtc;
        return Task.FromResult<SubscriptionOutboxEvent?>(outboxEvent);
    }

    public Task MarkEventPublishedAsync(
        string tenantId, string grantId, string eventId, string leaseId, DateTime publishedAtUtc, bool clearPayload,
        CancellationToken cancellationToken)
    {
        if (Leased(tenantId, grantId, eventId, leaseId) is { } outboxEvent)
        {
            outboxEvent.Status = SubscriptionOutboxStatus.Published;
            outboxEvent.PublishedAtUtc = publishedAtUtc;
            outboxEvent.LeaseId = null;
            outboxEvent.LeaseExpiresAtUtc = null;

            if (clearPayload)
            {
                outboxEvent.Payload = string.Empty;
            }
        }

        return Task.CompletedTask;
    }

    public Task MarkEventFailedAsync(
        string tenantId, string grantId, string eventId, string leaseId, SubscriptionOutboxStatus status,
        int attemptCount, DateTime nextAttemptAtUtc, string failureReason, CancellationToken cancellationToken)
    {
        if (Leased(tenantId, grantId, eventId, leaseId) is { } outboxEvent)
        {
            outboxEvent.Status = status;
            outboxEvent.AttemptCount = attemptCount;
            outboxEvent.NextAttemptAtUtc = nextAttemptAtUtc;
            outboxEvent.LastError = failureReason;
            outboxEvent.LeaseId = null;
            outboxEvent.LeaseExpiresAtUtc = null;
        }

        return Task.CompletedTask;
    }

    private SubscriptionOutboxEvent? Leased(string tenantId, string grantId, string eventId, string leaseId) =>
        All.Find(grant => grant.TenantId == tenantId && grant.ItemId == grantId)?
            .OutboxEvents.Find(candidate => candidate.EventId == eventId && candidate.LeaseId == leaseId);

    public Task<IReadOnlyList<SubscriptionCreditGrant>> ListLiveAsync(
        string tenantId, string subscriptionId, string meterKey, int? seatNumber, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        LiveReads++;

        return Task.FromResult<IReadOnlyList<SubscriptionCreditGrant>>(
        [
            .. SubscriptionCreditGrant.InConsumptionOrder(All.Where(grant =>
                grant.SubscriptionId == subscriptionId &&
                grant.MeterKey == meterKey && grant.SeatNumber == seatNumber && grant.IsLiveAt(nowUtc)))
        ]);
    }

    public Task<IReadOnlyList<SubscriptionCreditGrant>> ListAsync(
        string tenantId, string subscriptionId, string? meterKey, int? seatNumber, bool poolOnly,
        CreditGrantStatus? status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionCreditGrant>>([.. All]);

    public Task<IReadOnlyList<SubscriptionCreditGrant>> ListLiveForOrganizationAsync(
        string tenantId, string organizationId, string meterKey, DateTime nowUtc, CancellationToken cancellationToken)
    {
        LiveReads++;

        return Task.FromResult<IReadOnlyList<SubscriptionCreditGrant>>(
        [
            .. SubscriptionCreditGrant.InConsumptionOrder(All.Where(grant =>
                grant.IsOrganizationOwned && grant.OrganizationId == organizationId &&
                grant.MeterKey == meterKey && grant.IsLiveAt(nowUtc)))
        ]);
    }

    public Task<IReadOnlyList<SubscriptionCreditGrant>> ListForOrganizationAsync(
        string tenantId, string organizationId, string? meterKey, CreditGrantStatus? status,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionCreditGrant>>(
        [
            .. All.Where(grant =>
                    grant.IsOrganizationOwned && grant.OrganizationId == organizationId &&
                    (meterKey is null || grant.MeterKey == meterKey) &&
                    (status is null || grant.Status == status))
                .OrderByDescending(grant => grant.GrantedAtUtc)
        ]);

    public Task<SubscriptionCreditGrant?> TryConsumeAsync(
        string tenantId, string grantId, decimal quantity, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var grant = All.Find(candidate => candidate.ItemId == grantId);

        if (grant is null || !grant.IsLiveAt(nowUtc) || grant.Remaining < quantity)
        {
            return Task.FromResult<SubscriptionCreditGrant?>(null);
        }

        grant.Remaining -= quantity;

        if (grant.Remaining == 0)
        {
            grant.Status = CreditGrantStatus.Exhausted;
        }

        AfterConsume?.Invoke();
        return Task.FromResult<SubscriptionCreditGrant?>(grant);
    }

    public Task<IReadOnlyList<SubscriptionCreditGrant>> ListDueForCheckAsync(
        string tenantId, DateTime nowUtc, DateTime checkedBeforeUtc, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionCreditGrant>>(
        [
            .. All.Where(grant =>
                    grant.Status == CreditGrantStatus.Active &&
                    (grant.ExpiresAtUtc <= nowUtc ||
                     grant.LastCheckedAtUtc is null ||
                     grant.LastCheckedAtUtc < checkedBeforeUtc))
                .Take(limit)
        ]);

    public Task<bool> TryCloseAsync(
        string tenantId, string grantId, CreditGrantStatus status, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var grant = All.Find(candidate => candidate.ItemId == grantId);

        if (grant is not { Status: CreditGrantStatus.Active })
        {
            return Task.FromResult(false);
        }

        grant.Status = status;
        grant.ClosedAtUtc = nowUtc;
        grant.LastCheckedAtUtc = nowUtc;
        return Task.FromResult(true);
    }

    public Task MarkCheckedAsync(
        string tenantId, IReadOnlyCollection<string> grantIds, DateTime nowUtc, CancellationToken cancellationToken)
    {
        foreach (var grant in All.Where(grant => grantIds.Contains(grant.ItemId)))
        {
            grant.LastCheckedAtUtc = nowUtc;
        }

        return Task.CompletedTask;
    }

    public Task<bool> RestoreAsync(
        string tenantId, string grantId, decimal quantity, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var grant = All.Find(candidate => candidate.ItemId == grantId);

        if (grant is null || grant.Status is CreditGrantStatus.Expired or CreditGrantStatus.Forfeited)
        {
            return Task.FromResult(false);
        }

        grant.Remaining += quantity;
        grant.Status = CreditGrantStatus.Active;
        return Task.FromResult(true);
    }
}

internal sealed class InMemoryCreditPurchases : ISubscriptionCreditPurchaseRepository
{
    public List<SubscriptionCreditPurchase> All { get; } = [];

    public Task<bool> TryCreateAsync(SubscriptionCreditPurchase purchase, CancellationToken cancellationToken)
    {
        if (All.Exists(existing => existing.ItemId == purchase.ItemId))
        {
            return Task.FromResult(false);
        }

        All.Add(purchase);
        return Task.FromResult(true);
    }

    public Task<SubscriptionCreditPurchase?> GetAsync(string tenantId, string purchaseId, CancellationToken cancellationToken) =>
        Task.FromResult(All.Find(purchase => purchase.ItemId == purchaseId));

    public Task<bool> TryAdvanceAsync(
        SubscriptionCreditPurchase purchase, long expectedVersion, CancellationToken cancellationToken)
    {
        purchase.Version = expectedVersion + 1;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<SubscriptionCreditPurchase>> ListAsync(
        string tenantId, string subscriptionId, CreditPurchaseStatus? status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionCreditPurchase>>([.. All]);

    public Task<IReadOnlyList<SubscriptionCreditPurchase>> ListForOrganizationAsync(
        string tenantId, string organizationId, CreditPurchaseStatus? status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionCreditPurchase>>(
        [
            .. All.Where(purchase =>
                    purchase.IsOrganizationOwned && purchase.OrganizationId == organizationId &&
                    (status is null || purchase.Status == status))
                .OrderByDescending(purchase => purchase.CreatedAtUtc)
        ]);

    public Task<SubscriptionCreditPurchase?> FindOpenCheckoutForOrganizationAsync(
        string tenantId, string organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(All
            .Where(purchase => purchase.IsOrganizationOwned && purchase.OrganizationId == organizationId &&
                               purchase.PaymentPath == CreditPaymentPath.Checkout &&
                               purchase.Status is CreditPurchaseStatus.Pending or CreditPurchaseStatus.AwaitingPayment)
            .OrderByDescending(purchase => purchase.CreatedAtUtc)
            .FirstOrDefault());

    public Task<IReadOnlyList<SubscriptionCreditPurchase>> ListStaleAsync(
        string tenantId, IReadOnlyCollection<CreditPurchaseStatus> statuses, DateTime olderThanUtc, int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionCreditPurchase>>(
        [
            .. All.Where(purchase => statuses.Contains(purchase.Status) && purchase.LastUpdatedAtUtc < olderThanUtc)
                .Take(limit)
        ]);

    public Task<SubscriptionCreditPurchase?> FindOpenCheckoutAsync(
        string tenantId, string subscriptionId, CancellationToken cancellationToken) =>
        Task.FromResult(All
            .Where(purchase => purchase.SubscriptionId == subscriptionId &&
                               purchase.PaymentPath == CreditPaymentPath.Checkout &&
                               purchase.Status is CreditPurchaseStatus.Pending or CreditPurchaseStatus.AwaitingPayment)
            .OrderByDescending(purchase => purchase.CreatedAtUtc)
            .FirstOrDefault());

    public Task<SubscriptionCreditPurchase?> FindCheckoutByPaymentAsync(
        string tenantId, string paymentId, CancellationToken cancellationToken) =>
        Task.FromResult(All.Find(purchase =>
            purchase.PaymentId == paymentId && purchase.PaymentPath == CreditPaymentPath.Checkout));

    public Task<IReadOnlyList<SubscriptionCreditPurchase>> ListCardAdoptionDueAsync(
        string tenantId, DateTime createdAfterUtc, DateTime olderThanUtc, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionCreditPurchase>>(
        [
            .. All.Where(purchase =>
                    purchase.Status is CreditPurchaseStatus.Finalized or CreditPurchaseStatus.NeedsAttention &&
                    purchase.PaymentPath == CreditPaymentPath.Checkout &&
                    purchase.CardAdoptedAtUtc is null &&
                    purchase.CreatedAtUtc > createdAfterUtc &&
                    purchase.LastUpdatedAtUtc < olderThanUtc)
                .Take(limit)
        ]);
}
