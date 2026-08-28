using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Answers what an organization may do.
/// </summary>
/// <remarks>
/// Called on every gated action in every product built on this, so it reads only our own
/// database: the subscription, and a point read per metered entitlement. Note what this class
/// does not take in its constructor — no provider gateway, no HTTP client. That is not an
/// oversight but the guarantee itself, expressed where the compiler can hold it: if the
/// provider is down, every existing customer keeps working.
/// </remarks>
public sealed class EntitlementService : IEntitlementService
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionUsageRepository _usage;
    private readonly IMeterAllowanceResolver _allowances;
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly TimeProvider _time;

    public EntitlementService(
        ISubscriptionRepository subscriptions,
        ISubscriptionUsageRepository usage,
        IMeterAllowanceResolver allowances,
        ISubscriptionContextResolver contextResolver,
        IEntitlementSnapshotCache cache,
        TimeProvider? time = null)
    {
        _subscriptions = subscriptions;
        _usage = usage;
        _allowances = allowances;
        _contextResolver = contextResolver;
        _cache = cache;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionOperationResult<EntitlementSnapshotResponse>> GetAsync(
        bool fresh,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            organizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<EntitlementSnapshotResponse>(correlationId);
        }

        var context = resolution.Context!;
        var now = _time.GetUtcNow().UtcDateTime;
        var subscription = await LoadAsync(context, fresh, now, cancellationToken);

        // Re-evaluated against nowUtc every call, cache hit or miss: a subscription cached a few
        // seconds before its scheduled cancellation's CurrentPeriodEndUtc must stop granting the
        // instant that boundary passes, not merely once the cache entry itself expires.
        if (subscription is null || !SubscriptionLiveness.IsEffectivelyLive(subscription, now))
        {
            return SubscriptionOperationResult<EntitlementSnapshotResponse>.Success(
                NothingGranted(subscription),
                correlationId);
        }

        var balances = await BalancesAsync(subscription, cancellationToken);

        return SubscriptionOperationResult<EntitlementSnapshotResponse>.Success(
            Describe(subscription, balances),
            correlationId);
    }

    public async Task<SubscriptionOperationResult<EntitlementResponse>> GetAsync(
        string entitlementKey,
        bool fresh,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetAsync(fresh, organizationId, correlationId, cancellationToken);

        if (!snapshot.IsSuccess)
        {
            return snapshot.ToFailure<EntitlementResponse>();
        }

        var value = snapshot.Value!;

        var entitlement = value.Entitlements.Find(candidate =>
            string.Equals(candidate.Key, entitlementKey, StringComparison.Ordinal));

        // The reason matters as much as the answer: "no subscription", "subscription not
        // active" and "not on this plan" send a support engineer to three different places.
        var reason = !value.HasSubscription
            ? EntitlementReason.NoSubscription
            : value.Entitlements.Count == 0
                ? EntitlementReason.SubscriptionNotActive
                : EntitlementReason.NotInPlan;

        return SubscriptionOperationResult<EntitlementResponse>.Success(
            entitlement ?? Denied(entitlementKey, reason),
            correlationId);
    }

    private async Task<SubscriptionDetail?> LoadAsync(
        SubscriptionContext context,
        bool fresh,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (fresh)
        {
            _cache.Invalidate(context.TenantId, context.OrganizationId);
        }

        return await _cache.GetAsync(
            context.TenantId,
            context.OrganizationId,
            () => _subscriptions.GetLiveAsync(
                context.TenantId,
                context.OrganizationId,
                nowUtc,
                cancellationToken));
    }

    /// <summary>
    /// What a meter's window currently holds: how much is used, and — for a meter that carries
    /// unused allowance forward — what that window actually opened with.
    /// </summary>
    /// <remarks>
    /// The allowance is carried here only for carry-forward meters. For every other policy the
    /// entitlement's own declared limit stays the answer, which keeps this change from quietly
    /// redefining what a limit means on plans that do not use the feature.
    /// </remarks>
    private sealed record MeterReading(long Balance, long? WindowAllowance);

    /// <summary>
    /// The balance of every metered entitlement, read by identifier rather than searched for.
    /// </summary>
    private async Task<Dictionary<string, MeterReading>> BalancesAsync(
        SubscriptionDetail subscription,
        CancellationToken cancellationToken)
    {
        var balances = new Dictionary<string, MeterReading>(StringComparer.Ordinal);

        var now = _time.GetUtcNow().UtcDateTime;

        foreach (var meterKey in subscription.Plan.Entitlements
                     .Where(entitlement => entitlement.MeterKey is { Length: > 0 })
                     .Select(entitlement => entitlement.MeterKey!)
                     .Distinct(StringComparer.Ordinal))
        {
            var meter = subscription.Plan.Meters.Find(candidate =>
                string.Equals(candidate.MeterKey, meterKey, StringComparison.Ordinal));

            if (meter is null || !MeterPeriodResolver.TryGetPeriod(subscription, meter, now, out var period))
            {
                continue;
            }

            var counter = await _usage.GetCounterAsync(
                subscription.TenantId,
                SubscriptionUsageCounter.CreateId(
                    subscription.ItemId,
                    meterKey,
                    period.Key),
                cancellationToken);

            // Resolved rather than read off the counter. A window that has not recorded
            // anything yet has no counter and no snapshot, and answering with the plan's quantity
            // there advertised a smaller allowance than the usage gate would actually enforce —
            // until the first write seeded the counter, at which point the advertised limit jumped.
            balances[meterKey] = new MeterReading(
                counter?.Balance ?? 0,
                meter.ResetPolicy == MeterResetPolicy.CarryForward
                    ? await _allowances.EffectiveAsync(
                        subscription, meter, period, counter, cancellationToken)
                    : null);
        }

        return balances;
    }

    private static EntitlementSnapshotResponse NothingGranted(
        SubscriptionDetail? subscription) => new()
    {
        HasSubscription = subscription is not null,
        Status = subscription?.Status.ToString() ?? nameof(EntitlementReason.NoSubscription),
        PlanCode = subscription?.Plan.Code ?? string.Empty
    };

    private static EntitlementSnapshotResponse Describe(
        SubscriptionDetail subscription,
        Dictionary<string, MeterReading> balances) => new()
    {
        HasSubscription = true,
        Status = subscription.Status.ToString(),
        PlanCode = subscription.Plan.Code,
        CurrentPeriodEndUtc = subscription.CurrentPeriodEndUtc,
        TrialEndsAtUtc = subscription.Trial?.EndsAtUtc,
        FeaturesJson = subscription.Plan.FeaturesJson,
        Quantities = subscription.QuantityItems
            .Select(item => new EntitlementQuantityResponse
            {
                ItemKey = item.ItemKey,
                UnitLabel = item.UnitLabel,
                Quantity = item.Quantity
            })
            .ToList(),
        Entitlements = subscription.Plan.Entitlements
            .Select(entitlement => Describe(subscription, entitlement, balances))
            .ToList()
    };

    private static EntitlementResponse Describe(
        SubscriptionDetail subscription,
        PlanEntitlement entitlement,
        Dictionary<string, MeterReading> balances)
    {
        if (entitlement.LimitKind != EntitlementLimitKind.Count)
        {
            return new EntitlementResponse
            {
                Key = entitlement.Key,
                // Unlimited never reports a limit reached; a boolean entitlement present on the
                // plan is simply granted.
                Allowed = true,
                Reason = nameof(EntitlementReason.Allowed),
                LimitKind = entitlement.LimitKind.ToString(),
                UnitLabel = entitlement.UnitLabel
            };
        }

        var reading = entitlement.MeterKey is { Length: > 0 } meterKey &&
                      balances.TryGetValue(meterKey, out var found)
            ? found
            : null;

        // A carried-forward window opened with more than the plan's own quantity, and usage will
        // enforce that larger figure. Reporting the declared limit here would tell a caller it had
        // run out while the usage call still permitted the action — the exact disagreement
        // LimitFor's own remarks warn against.
        var limit = reading?.WindowAllowance ?? LimitFor(subscription, entitlement);
        var used = reading?.Balance ?? 0;

        var allowed = used < limit;

        return new EntitlementResponse
        {
            Key = entitlement.Key,
            Allowed = allowed,
            Reason = allowed
                ? nameof(EntitlementReason.Allowed)
                : nameof(EntitlementReason.LimitReached),
            LimitKind = entitlement.LimitKind.ToString(),
            Limit = limit,
            Used = used,
            Remaining = Math.Max(0, limit - used),
            UnitLabel = entitlement.UnitLabel
        };
    }

    /// <summary>
    /// A trial's grant replaces the plan's limit, matching how usage recording measures it.
    /// The two must agree or a caller is told it may act and then refused.
    /// </summary>
    private static long LimitFor(
        SubscriptionDetail subscription,
        PlanEntitlement entitlement)
    {
        var planLimit = entitlement.Limit ?? 0;

        if (subscription.Status != SubscriptionStatus.Trialing ||
            subscription.Trial is null ||
            entitlement.MeterKey is not { Length: > 0 } meterKey)
        {
            return planLimit;
        }

        var grant = subscription.Trial.Grants.Find(candidate =>
            string.Equals(candidate.MeterKey, meterKey, StringComparison.Ordinal));

        return grant?.IncludedQuantity ?? planLimit;
    }

    private static EntitlementResponse Denied(string key, EntitlementReason reason) => new()
    {
        Key = key,
        Allowed = false,
        Reason = reason.ToString(),
        LimitKind = nameof(EntitlementLimitKind.Boolean)
    };
}
