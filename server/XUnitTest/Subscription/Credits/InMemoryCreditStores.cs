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

    public Task<IReadOnlyList<SubscriptionCreditGrant>> ListLiveAsync(
        string tenantId, string subscriptionId, string meterKey, int? seatNumber, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        LiveReads++;

        return Task.FromResult<IReadOnlyList<SubscriptionCreditGrant>>(
        [
            .. SubscriptionCreditGrant.InConsumptionOrder(All.Where(grant =>
                grant.MeterKey == meterKey && grant.SeatNumber == seatNumber && grant.IsLiveAt(nowUtc)))
        ]);
    }

    public Task<IReadOnlyList<SubscriptionCreditGrant>> ListAsync(
        string tenantId, string subscriptionId, string? meterKey, int? seatNumber, bool poolOnly,
        CreditGrantStatus? status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionCreditGrant>>([.. All]);

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

    public Task<IReadOnlyList<SubscriptionCreditPurchase>> ListStaleAsync(
        string tenantId, IReadOnlyCollection<CreditPurchaseStatus> statuses, DateTime olderThanUtc, int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionCreditPurchase>>(
        [
            .. All.Where(purchase => statuses.Contains(purchase.Status) && purchase.LastUpdatedAtUtc < olderThanUtc)
                .Take(limit)
        ]);
}
