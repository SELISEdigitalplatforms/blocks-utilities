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
    private readonly TimeProvider _time;

    public SubscriptionMemberService(
        ISubscriptionRepository subscriptions,
        ISubscriptionAssignmentRepository assignments,
        ISubscriptionContextResolver contextResolver,
        IEntitlementSnapshotCache cache,
        TimeProvider? time = null)
    {
        _subscriptions = subscriptions;
        _assignments = assignments;
        _contextResolver = contextResolver;
        _cache = cache;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionOperationResult<SubscriptionMemberResponse>> AssignAsync(
        string subscriptionId,
        AssignMemberRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolved = await ResolveAsync<SubscriptionMemberResponse>(
            subscriptionId, correlationId, cancellationToken);

        if (resolved.Failure is { } failure)
        {
            return failure;
        }

        var (context, subscription) = resolved.Value;

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return Failure<SubscriptionMemberResponse>(
                PaymentFailureKind.Validation,
                "subscription_member_required",
                "Name the person the seat is for.",
                correlationId);
        }

        var purchased = subscription.QuantityItems.Count == 0
            ? 1
            : PurchasedMembersOf(subscription);

        if (purchased is null)
        {
            return Failure<SubscriptionMemberResponse>(
                PaymentFailureKind.Validation,
                "subscription_member_count_ambiguous",
                "This plan does not say how many people it is for. Mark exactly one quantity as " +
                    "the one that counts them, and on a flat-priced plan give that quantity a " +
                    "maximum.",
                correlationId);
        }

        // Read before writing, and the write is what decides. A count is a moment old by the time
        // it is acted on, so two administrators filling the last seat together would both pass
        // this; the unique index refuses the loser. This exists to give an honest answer to the
        // ordinary case, not to make the race safe.
        if (await _assignments.CountActiveAsync(
                context.TenantId, subscription.ItemId, cancellationToken) >= purchased)
        {
            return Failure<SubscriptionMemberResponse>(
                PaymentFailureKind.Conflict,
                "subscription_members_exhausted",
                "Every seat on this subscription is taken. Release one, or buy more.",
                correlationId);
        }

        var assignment = new SubscriptionAssignment
        {
            TenantId = context.TenantId,
            OrganizationId = subscription.OrganizationId,
            SubscriptionId = subscription.ItemId,
            UserId = request.UserId.Trim(),
            AssignedAtUtc = _time.GetUtcNow().UtcDateTime,
            AssignedByUserId = context.UserId,
            CorrelationId = correlationId
        };

        var outcome = await _assignments.TryAssignAsync(assignment, cancellationToken);

        if (outcome == MemberAssignmentOutcome.AlreadyHeld)
        {
            return Failure<SubscriptionMemberResponse>(
                PaymentFailureKind.Conflict,
                "subscription_member_already_assigned",
                "This person already holds a seat on this subscription.",
                correlationId);
        }

        // What the new holder may do has changed, and every subscriber in the organization caches
        // the organization's own subscription alongside their own seats.
        _cache.Invalidate(context.TenantId, subscription.OrganizationId);

        return SubscriptionOperationResult<SubscriptionMemberResponse>.Success(
            Describe(assignment), correlationId);
    }

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
