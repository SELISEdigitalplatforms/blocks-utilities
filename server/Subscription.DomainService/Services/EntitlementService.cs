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
    private readonly ISubscriptionAssignmentRepository _assignments;
    private readonly ISubscriptionUsageRepository _usage;
    private readonly IMeterAllowanceResolver _allowances;
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly TimeProvider _time;

    public EntitlementService(
        ISubscriptionRepository subscriptions,
        ISubscriptionAssignmentRepository assignments,
        ISubscriptionUsageRepository usage,
        IMeterAllowanceResolver allowances,
        ISubscriptionContextResolver contextResolver,
        IEntitlementSnapshotCache cache,
        TimeProvider? time = null)
    {
        _subscriptions = subscriptions;
        _assignments = assignments;
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
        var subscriptions = await LoadAsync(context, fresh, now, cancellationToken);

        // Re-evaluated against nowUtc every call, cache hit or miss: a subscription cached a few
        // seconds before its scheduled cancellation's CurrentPeriodEndUtc must stop granting the
        // instant that boundary passes, not merely once the cache entry itself expires.
        var live = subscriptions
            .Where(subscription => SubscriptionLiveness.IsEffectivelyLive(subscription, now))
            .ToList();

        if (live.Count == 0)
        {
            return SubscriptionOperationResult<EntitlementSnapshotResponse>.Success(
                NothingGranted(subscriptions.Count > 0 ? subscriptions[0] : null),
                correlationId);
        }

        var described = new List<EntitlementSnapshotResponse>(live.Count);

        foreach (var subscription in live)
        {
            // Per subscription, never pooled: a meter's balance belongs to the subscription that
            // recorded it, and rating one plan's usage against another's counter would bill the
            // wrong allowance.
            var balances = await BalancesAsync(subscription, cancellationToken);

            described.Add(Describe(subscription, balances, now));
        }

        return SubscriptionOperationResult<EntitlementSnapshotResponse>.Success(
            Merge(described),
            correlationId);
    }

    /// <summary>
    /// One answer from a subscriber's own plan and their organization's, theirs winning per key.
    /// </summary>
    /// <remarks>
    /// A fallback, not a choice between the two. The plans cover different things -- an
    /// organization-wise plan what the organization shares, a user-wise plan one person's own
    /// allowance -- so a key the subscriber's plan says nothing about still resolves against the
    /// organization's. Returning only the subscriber's would revoke everything shared the moment
    /// they were given a plan of their own.
    /// <para>
    /// Where both declare the same key the subscriber's wins, which is the order
    /// <see cref="ISubscriptionRepository.ListLiveForSubscriberAsync"/> returns them in and the
    /// same precedence the catalogue already applies to an organization's plan over the tenant's.
    /// </para>
    /// <para>
    /// The scalars describe that same first subscription rather than being merged, because a
    /// status, a plan code and a period end belong to one subscription and averaging them would
    /// describe neither. A subscriber holding only their organization's plan therefore sees exactly
    /// what they see today.
    /// </para>
    /// </remarks>
    private static EntitlementSnapshotResponse Merge(
        IReadOnlyList<EntitlementSnapshotResponse> described)
    {
        if (described.Count == 1)
        {
            return described[0];
        }

        var primary = described[0];

        return new EntitlementSnapshotResponse
        {
            HasSubscription = true,
            Status = primary.Status,
            PlanCode = primary.PlanCode,
            CurrentPeriodEndUtc = primary.CurrentPeriodEndUtc,
            TrialEndsAtUtc = primary.TrialEndsAtUtc,
            FeaturesJson = primary.FeaturesJson,
            Quantities =
            [
                .. described
                    .SelectMany(snapshot => snapshot.Quantities)
                    .DistinctBy(quantity => quantity.ItemKey, StringComparer.Ordinal)
            ],
            Entitlements =
            [
                .. described
                    .SelectMany(snapshot => snapshot.Entitlements)
                    .DistinctBy(entitlement => entitlement.Key, StringComparer.Ordinal)
            ]
        };
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

    /// <summary>
    /// Everything granting something to this caller: the seats they hold, then their
    /// organization's own subscription.
    /// </summary>
    /// <remarks>
    /// Both, never one or the other. An organization-wise plan covers what the organization shares
    /// and a seat covers one person's own allowance, so resolving only the seats would revoke
    /// everything shared the moment somebody was given one.
    /// <para>
    /// Seats first, because that is the precedence a reader applies where both plans declare the
    /// same key — the more specific purchase answers.
    /// </para>
    /// <para>
    /// A caller with no user — background work, a machine token — holds no seats and resolves the
    /// organization's subscription alone, which is exactly what every caller gets today.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<SubscriptionDetail>> LoadAsync(
        SubscriptionContext context,
        bool fresh,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (fresh)
        {
            _cache.Invalidate(context.TenantId, context.OrganizationId);
        }

        var subscriberUserId = context.UserId ?? string.Empty;

        return await _cache.GetAsync(
            context.TenantId,
            context.OrganizationId,
            subscriberUserId,
            () => ResolveAsync(context, subscriberUserId, nowUtc, cancellationToken));
    }

    /// <summary>
    /// Reads the seats and the organization's subscription, and puts them in precedence order.
    /// </summary>
    /// <remarks>
    /// A subscription reached through a seat is not read a second time if it is also the
    /// organization's own. That cannot happen while an organization-wise subscription carries no
    /// seats, but the guard costs nothing and a duplicate would silently double a plan's
    /// entitlements in the merge downstream.
    /// </remarks>
    private async Task<IReadOnlyList<SubscriptionDetail>> ResolveAsync(
        SubscriptionContext context,
        string subscriberUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var heldSeatIds = subscriberUserId.Length == 0
            ? []
            : await _assignments.ListSubscriptionIdsForUserAsync(
                context.TenantId,
                context.OrganizationId,
                subscriberUserId,
                cancellationToken);

        var held = await _subscriptions.ListLiveByIdsAsync(
            context.TenantId, heldSeatIds, nowUtc, cancellationToken);

        var organization = await _subscriptions.GetLiveAsync(
            context.TenantId, context.OrganizationId, nowUtc, cancellationToken);

        if (organization is null ||
            held.Any(seat => string.Equals(
                seat.ItemId, organization.ItemId, StringComparison.Ordinal)))
        {
            return held;
        }

        return [.. held, organization];
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
    private sealed record MeterReading(decimal Balance, decimal? WindowAllowance);

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
        Dictionary<string, MeterReading> balances,
        DateTime now) => new()
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
            .Select(entitlement => Describe(subscription, entitlement, balances, now))
            .ToList()
    };

    private static EntitlementResponse Describe(
        SubscriptionDetail subscription,
        PlanEntitlement entitlement,
        Dictionary<string, MeterReading> balances,
        DateTime now)
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
        var limit = reading?.WindowAllowance ?? LimitFor(subscription, entitlement, now);
        var used = reading?.Balance ?? 0;

        // Past the limit is still allowed when the meter bills what goes beyond it, exactly as
        // recording decides: it refuses over-allowance usage only on a meter with no overage.
        // Answering LimitReached there told a caller to stop while the usage call would have
        // accepted and billed the same usage, so nothing checking first could ever reach overage.
        // A campaign's temporary cap is the exception -- the offer's own limit, not an allowance
        // -- and stays hard while it is in force.
        var overageAllowed = CampaignLimitFor(subscription, entitlement, now) is null &&
            entitlement.MeterKey is { Length: > 0 } meteredBy &&
            subscription.Plan.Meters.Exists(meter =>
                meter.OverageAllowed &&
                string.Equals(meter.MeterKey, meteredBy, StringComparison.Ordinal));

        var allowed = used < limit || overageAllowed;

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
            OverageAllowed = overageAllowed,
            UnitLabel = entitlement.UnitLabel
        };
    }

    /// <summary>
    /// A trial's grant replaces the plan's limit, matching how usage recording measures it.
    /// The two must agree or a caller is told it may act and then refused.
    /// </summary>
    /// <remarks>
    /// Internal so the published entitlements read model reports the same figure; it copied the
    /// plan's raw limit and showed a trialing subscription the paid allowance, not its grant.
    /// </remarks>
    internal static decimal LimitFor(
        SubscriptionDetail subscription,
        PlanEntitlement entitlement,
        DateTime now)
    {
        var planLimit = entitlement.Limit ?? 0;

        if (CampaignLimitFor(subscription, entitlement, now) is { } campaignLimit)
        {
            return campaignLimit;
        }

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

    /// <summary>The campaign's temporary cap on this entitlement, while one is in force.</summary>
    private static decimal? CampaignLimitFor(
        SubscriptionDetail subscription,
        PlanEntitlement entitlement,
        DateTime now)
    {
        // A free-opening-period campaign's temporary cap, in force only while the campaign's own
        // opening period is still running. Evaluated against the clock on every call rather than
        // read off a stored flag, the same way SubscriptionLiveness.IsEffectivelyLive above already
        // is -- so the plan's ordinary limit resumes the instant CurrentPeriodEndUtc passes, with
        // nothing that has to run at exactly that moment for it to happen. CurrentPeriodEndUtc is
        // the campaign's own opening-period boundary here: a monthly, calendar-aligned price's
        // first period already ends there by construction, so no separate boundary needs storing.
        if (subscription.Discount is
            {
                Campaign:
                {
                    Kind: CampaignKind.FreeOpeningCalendarPeriod,
                    EntitlementOverride: { } campaignOverride
                }
            } &&
            string.Equals(campaignOverride.EntitlementKey, entitlement.Key, StringComparison.Ordinal) &&
            now < subscription.CurrentPeriodEndUtc)
        {
            return campaignOverride.Limit;
        }

        return null;
    }

    private static EntitlementResponse Denied(string key, EntitlementReason reason) => new()
    {
        Key = key,
        Allowed = false,
        Reason = reason.ToString(),
        LimitKind = nameof(EntitlementLimitKind.Boolean)
    };
}
