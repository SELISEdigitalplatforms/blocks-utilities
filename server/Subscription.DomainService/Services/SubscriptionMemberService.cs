using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Gives out and takes back the seats a subscription was bought with.
/// </summary>
/// <remarks>
/// How many seats exist is not this service's to decide: it is
/// <see cref="SubscriptionQuantityItem.Quantity"/>, which was priced and charged when the
/// subscription was bought or last changed. This only decides who sits in them, and refuses to seat
/// more people than were paid for.
/// </remarks>
public sealed class SubscriptionMemberService : ISubscriptionMemberService
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionAssignmentRepository _assignments;
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly ISubscriptionUsageRepository? _usage;
    private readonly IMeterAllowanceResolver? _allowances;
    private readonly TimeProvider _time;

    public SubscriptionMemberService(
        ISubscriptionRepository subscriptions,
        ISubscriptionAssignmentRepository assignments,
        ISubscriptionContextResolver contextResolver,
        IEntitlementSnapshotCache cache,
        TimeProvider? time = null,
        // Optional together: without them a free seat cannot be told from another free seat, and
        // the lowest free number is what this handed out before seats counted their own usage.
        ISubscriptionUsageRepository? usage = null,
        IMeterAllowanceResolver? allowances = null)
    {
        _subscriptions = subscriptions;
        _assignments = assignments;
        _contextResolver = contextResolver;
        _cache = cache;
        _usage = usage;
        _allowances = allowances;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionOperationResult<SubscriptionMemberAssignmentResponse>> AssignAsync(
        string subscriptionId,
        AssignMemberRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolved = await ResolveAsync<SubscriptionMemberAssignmentResponse>(
            subscriptionId, correlationId, cancellationToken);

        if (resolved.Failure is { } failure)
        {
            return failure;
        }

        var (context, subscription) = resolved.Value;

        var named = request.UserIds
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Select(userId => userId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (named.Count == 0)
        {
            return Failure<SubscriptionMemberAssignmentResponse>(
                PaymentFailureKind.Validation,
                "subscription_member_required",
                "Name at least one person.",
                correlationId);
        }

        var purchased = subscription.QuantityItems.Count == 0
            ? 1
            : PurchasedMembersOf(subscription);

        // A decrease already scheduled is the number that matters, when it is the smaller one.
        // Filling seats that are about to be taken away would leave people assigned to seats the
        // subscription no longer has the moment the period turns over — and a decrease cannot be
        // refunded, so it will turn over.
        if (purchased is { } bought && PendingMembersOf(subscription) is { } pending)
        {
            purchased = Math.Min(bought, pending);
        }

        if (purchased is null)
        {
            return Failure<SubscriptionMemberAssignmentResponse>(
                PaymentFailureKind.Validation,
                "subscription_member_count_ambiguous",
                "This plan does not say how many people it is for. Mark exactly one quantity as " +
                    "the one that counts them, and on a flat-priced plan give that quantity a " +
                    "maximum.",
                correlationId);
        }

        var assigned = new List<SubscriptionMemberResponse>();
        var refused = new List<SubscriptionMemberRefusalResponse>();

        // Read once and tracked in memory, rather than re-read per person: nothing this call
        // writes is visible to a fresh read until it lands, so a ten-name batch would walk past a
        // two-seat subscription.
        var held = await _assignments.ListActiveAsync(
            context.TenantId, subscription.ItemId, cancellationToken);

        var taken = held
            .Select(assignment => assignment.SeatNumber)
            .ToHashSet();

        // Ordered once for the whole batch, because ordering reads counters and the order cannot
        // change while this call runs: nothing else is handing out seats, and what this call
        // assigns is tracked below.
        var offered = new Queue<int>(await OfferSeatsAsync(
            subscription,
            FreeSeats(taken, purchased.Value),
            _time.GetUtcNow().UtcDateTime,
            cancellationToken));

        foreach (var userId in named)
        {
            var seat = offered.Count == 0 ? (int?)null : offered.Peek();

            if (seat is null)
            {
                refused.Add(Refusal(userId, "subscription_member_limit_reached",
                    "This subscription already has all the people it was bought for."));
                continue;
            }

            var assignment = new SubscriptionAssignment
            {
                TenantId = context.TenantId,
                OrganizationId = subscription.OrganizationId,
                SubscriptionId = subscription.ItemId,
                UserId = userId,
                SeatNumber = seat.Value,
                AssignedAtUtc = _time.GetUtcNow().UtcDateTime,
                AssignedByUserId = context.UserId,
                CorrelationId = correlationId
            };

            var outcome = await _assignments.TryAssignAsync(assignment, cancellationToken);

            if (outcome == MemberAssignmentOutcome.AlreadyHeld)
            {
                // Not counted against the remainder: they were already occupying a place, so the
                // count this started from already included them.
                refused.Add(Refusal(userId, "subscription_member_already_assigned",
                    "This person is already on this subscription."));
                continue;
            }

            offered.Dequeue();
            taken.Add(seat.Value);
            assigned.Add(Describe(assignment));
        }

        if (assigned.Count > 0)
        {
            // What these people may do has changed, and every subscriber in the organization
            // caches the organization's own subscription alongside their own places.
            _cache.Invalidate(context.TenantId, subscription.OrganizationId);
        }

        return SubscriptionOperationResult<SubscriptionMemberAssignmentResponse>.Success(
            new SubscriptionMemberAssignmentResponse
            {
                SubscriptionId = subscription.ItemId,
                Assigned = assigned,
                Refused = refused
            },
            correlationId);
    }

    /// <summary>
    /// Every seat bought that nobody is sitting in.
    /// </summary>
    private static List<int> FreeSeats(HashSet<int> taken, long purchased)
    {
        var free = new List<int>();

        for (var seat = 1; seat <= purchased; seat++)
        {
            if (!taken.Contains(seat))
            {
                free.Add(seat);
            }
        }

        return free;
    }

    /// <summary>
    /// The free seats in the order newcomers should be given them: the one with the most allowance
    /// left first.
    /// </summary>
    /// <remarks>
    /// A seat carries its own allowance and keeps whatever the last person spent of it, so free
    /// seats are not interchangeable. Handing out the lowest free number would give a newcomer
    /// whatever somebody else left of it while an untouched seat sat empty beside it — and since
    /// the allowance rides to the period boundary, that person would be short for the rest of the
    /// period with no way to tell why.
    /// <para>
    /// Summed across the meters, because a seat is one thing to hand out and the plan may meter
    /// several: a seat has to be ranked as a whole or the answer depends on which meter is asked.
    /// Ties keep the lowest number, which is what an administrator can predict, and a subscription
    /// nobody has used yet is entirely ties.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<int>> OfferSeatsAsync(
        SubscriptionDetail subscription,
        List<int> free,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (_usage is null || _allowances is null || free.Count < 2)
        {
            return free;
        }

        var windows = new List<(PlanMeter Meter, BillingPeriod Period)>();

        foreach (var meter in subscription.Plan.Meters)
        {
            if (MeterPeriodResolver.TryGetPeriod(subscription, meter, nowUtc, out var period))
            {
                windows.Add((meter, period));
            }
        }

        if (windows.Count == 0)
        {
            return free;
        }

        // One round trip for every seat and window at once. A seat with no counter has spent
        // nothing of that window, which is the common case and needs no read of its own.
        var counters = await _usage.GetCountersAsync(
            subscription.TenantId,
            free.SelectMany(seat => windows.Select(window => SubscriptionUsageCounter.CreateId(
                    subscription.ItemId, window.Meter.MeterKey, window.Period.Key, seat)))
                .ToList(),
            cancellationToken);

        var remaining = new Dictionary<int, decimal>(free.Count);

        foreach (var seat in free)
        {
            decimal left = 0;

            foreach (var (meter, period) in windows)
            {
                counters.TryGetValue(
                    SubscriptionUsageCounter.CreateId(
                        subscription.ItemId, meter.MeterKey, period.Key, seat),
                    out var counter);

                // The effective allowance rather than the plan's included quantity: a seat that
                // saved last window opens this one with more, and ignoring that would rank a seat
                // below one holding less.
                var allowance = await _allowances.EffectiveAsync(
                    subscription, meter, period, counter, cancellationToken, seat);

                left += Math.Max(0, allowance - (counter?.Balance ?? 0));
            }

            remaining[seat] = left;
        }

        return free
            .OrderByDescending(seat => remaining[seat])
            .ThenBy(seat => seat)
            .ToList();
    }

    private static SubscriptionMemberRefusalResponse Refusal(
        string userId,
        string reasonCode,
        string reason) => new()
    {
        UserId = userId,
        ReasonCode = reasonCode,
        Reason = reason
    };

    public async Task<SubscriptionOperationResult<SubscriptionMemberResponse>> ReleaseAsync(
        string subscriptionId,
        string userId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync<SubscriptionMemberResponse>(
            subscriptionId, correlationId, cancellationToken, requireLive: false);

        if (resolved.Failure is { } failure)
        {
            return failure;
        }

        var (context, subscription) = resolved.Value;
        var releasedAtUtc = _time.GetUtcNow().UtcDateTime;

        var outcome = await _assignments.TryReleaseAsync(
            context.TenantId, subscription.ItemId, userId, releasedAtUtc, cancellationToken);

        if (outcome == MemberReleaseOutcome.NotHeld)
        {
            return Failure<SubscriptionMemberResponse>(
                PaymentFailureKind.NotFound,
                "subscription_member_not_assigned",
                "This person does not hold a seat on this subscription.",
                correlationId);
        }

        _cache.Invalidate(context.TenantId, subscription.OrganizationId);

        return SubscriptionOperationResult<SubscriptionMemberResponse>.Success(
            new SubscriptionMemberResponse
            {
                SubscriptionId = subscription.ItemId,
                UserId = userId,
                ReleasedAtUtc = releasedAtUtc
            },
            correlationId);
    }

    public async Task<SubscriptionOperationResult<SubscriptionMembersResponse>> ListAsync(
        string subscriptionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync<SubscriptionMembersResponse>(
            subscriptionId, correlationId, cancellationToken, requireLive: false);

        if (resolved.Failure is { } failure)
        {
            return failure;
        }

        var (context, subscription) = resolved.Value;

        var held = await _assignments.ListActiveAsync(
            context.TenantId, subscription.ItemId, cancellationToken);

        var purchased = (subscription.QuantityItems.Count == 0
            ? 1
            : PurchasedMembersOf(subscription)) ?? 0;

        return SubscriptionOperationResult<SubscriptionMembersResponse>.Success(
            new SubscriptionMembersResponse
            {
                SubscriptionId = subscription.ItemId,
                Purchased = purchased,
                Held = held.Count,
                Available = Math.Max(0, purchased - held.Count),
                Seats = [.. held.Select(Describe)]
            },
            correlationId);
    }

    /// <summary>
    /// How many people this subscription may have, or null when nothing says.
    /// </summary>
    /// <remarks>
    /// Which number answers depends on how the plan charges, because only one of the two means
    /// anything in each case.
    /// <para>
    /// When the price multiplies a quantity — <see cref="PriceSnapshot.QuantityItemKey"/> naming
    /// the item that counts people — the subscription paid for exactly that many, so
    /// <see cref="SubscriptionQuantityItem.Quantity"/> is the answer.
    /// </para>
    /// <para>
    /// When the price is flat it multiplies nothing, and the quantity stored against the item is a
    /// number nobody charged for or interacted with. What was sold is "this plan, for up to N
    /// people", and N is <see cref="SubscriptionQuantityItem.MaxQuantity"/>. Reading Quantity there
    /// would give one person a plan bought for ten, because the default was never changed and
    /// changing it would have cost nothing.
    /// </para>
    /// <para>
    /// A flat plan with no ceiling is refused rather than treated as unlimited. Unlimited is the
    /// honest reading of "flat fee, no cap", and it is also the reading where an unnoticed omission
    /// gives the product away, so it has to be said deliberately.
    /// </para>
    /// </remarks>
    private static long? PurchasedMembersOf(SubscriptionDetail subscription)
    {
        var counting = CountingItemOf(subscription);

        if (counting is null)
        {
            return null;
        }

        var pricedPerMember = !string.IsNullOrWhiteSpace(subscription.Price.QuantityItemKey) &&
            string.Equals(
                subscription.Price.QuantityItemKey,
                counting.ItemKey,
                StringComparison.Ordinal);

        return pricedPerMember ? counting.Quantity : counting.MaxQuantity;
    }

    /// <summary>
    /// How many people a scheduled decrease will leave room for, or null when none is scheduled.
    /// </summary>
    /// <remarks>
    /// Read from the pending change rather than the subscription, because a decrease takes effect
    /// at the period end and the subscription still carries what was paid for until then.
    /// </remarks>
    private static long? PendingMembersOf(SubscriptionDetail subscription)
    {
        if (subscription.PendingQuantityChange is not { } pending)
        {
            return null;
        }

        var counting = CountingItemOf(subscription);

        if (counting is null)
        {
            return null;
        }

        return pending.RequestedQuantities
            .Where(item => string.Equals(
                item.ItemKey, counting.ItemKey, StringComparison.Ordinal))
            .Select(item => (long?)item.Quantity)
            .FirstOrDefault();
    }

    /// <summary>
    /// The quantity item that says how many people, or null when the plan does not say.
    /// </summary>
    /// <remarks>
    /// A plan selling one quantity needs no mark, which keeps every plan authored before this from
    /// needing an edit. A plan selling none is one person's plan and is handled by the caller.
    /// </remarks>
    private static SubscriptionQuantityItem? CountingItemOf(SubscriptionDetail subscription)
    {
        if (subscription.QuantityItems.Count == 1)
        {
            return subscription.QuantityItems[0];
        }

        var marked = subscription.QuantityItems
            .Where(item => item.CountsMembers)
            .ToList();

        return marked.Count == 1 ? marked[0] : null;
    }

    /// <summary>
    /// Resolves the caller and the subscription they named, refusing anything not seat-based.
    /// </summary>
    /// <remarks>
    /// Read through the organization rather than by id alone, so a subscription belonging to
    /// somebody else cannot be seated by naming its identifier.
    /// </remarks>
    private async Task<(SubscriptionOperationResult<T>? Failure,
        (SubscriptionContext Context, SubscriptionDetail Subscription) Value)> ResolveAsync<T>(
        string subscriptionId,
        string correlationId,
        CancellationToken cancellationToken,
        bool requireLive = true)
    {
        var resolution = await _contextResolver.ResolveAsync(
            correlationId, null, cancellationToken);

        if (!resolution.IsSuccess)
        {
            return (resolution.ToFailure<T>(correlationId), default);
        }

        var context = resolution.Context!;

        var subscription = await _subscriptions.GetAsync(
            context.TenantId, context.OrganizationId, subscriptionId, cancellationToken);

        if (subscription is null)
        {
            return (Failure<T>(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "That subscription does not exist for this organization.",
                correlationId), default);
        }

        if (subscription.Plan.SubscriberScope != SubscriberScope.User)
        {
            return (Failure<T>(
                PaymentFailureKind.Validation,
                "subscription_not_member_based",
                "This subscription belongs to the organization itself, so it has no seats to " +
                    "give out.",
                correlationId), default);
        }

        // Releasing and listing stay available on a subscription that has stopped granting: an
        // administrator still needs to see who was on it, and to take a seat back from somebody
        // who has left, after it lapses.
        if (requireLive &&
            !SubscriptionLiveness.IsEffectivelyLive(subscription, _time.GetUtcNow().UtcDateTime))
        {
            return (Failure<T>(
                PaymentFailureKind.Conflict,
                "subscription_not_live",
                "This subscription no longer grants anything, so a seat on it would grant " +
                    "nothing either.",
                correlationId), default);
        }

        return (null, (context, subscription));
    }

    private static SubscriptionOperationResult<T> Failure<T>(
        PaymentFailureKind kind,
        string code,
        string message,
        string correlationId) =>
        SubscriptionOperationResult<T>.Failure(kind, code, message, correlationId);

    private static SubscriptionMemberResponse Describe(SubscriptionAssignment assignment) => new()
    {
        SubscriptionId = assignment.SubscriptionId,
        UserId = assignment.UserId,
        AssignedAtUtc = assignment.AssignedAtUtc,
        ReleasedAtUtc = assignment.ReleasedAtUtc
    };
}
