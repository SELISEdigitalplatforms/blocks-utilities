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
public sealed class SubscriptionSeatService : ISubscriptionSeatService
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionAssignmentRepository _assignments;
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly TimeProvider _time;

    public SubscriptionSeatService(
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

    public async Task<SubscriptionOperationResult<SubscriptionSeatResponse>> AssignAsync(
        string subscriptionId,
        AssignSeatRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolved = await ResolveAsync<SubscriptionSeatResponse>(
            subscriptionId, correlationId, cancellationToken);

        if (resolved.Failure is { } failure)
        {
            return failure;
        }

        var (context, subscription) = resolved.Value;

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return Failure<SubscriptionSeatResponse>(
                PaymentFailureKind.Validation,
                "subscription_seat_user_required",
                "Name the person the seat is for.",
                correlationId);
        }

        var purchased = PurchasedSeatsOf(subscription);

        if (purchased is null)
        {
            return Failure<SubscriptionSeatResponse>(
                PaymentFailureKind.Validation,
                "subscription_seat_count_ambiguous",
                "This plan sells several quantities and none of them — or more than one — is " +
                    "marked as the one that counts people. Mark exactly one.",
                correlationId);
        }

        // Read before writing, and the write is what decides. A count is a moment old by the time
        // it is acted on, so two administrators filling the last seat together would both pass
        // this; the unique index refuses the loser. This exists to give an honest answer to the
        // ordinary case, not to make the race safe.
        if (await _assignments.CountActiveAsync(
                context.TenantId, subscription.ItemId, cancellationToken) >= purchased)
        {
            return Failure<SubscriptionSeatResponse>(
                PaymentFailureKind.Conflict,
                "subscription_seats_exhausted",
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

        if (outcome == SeatAssignmentOutcome.AlreadyHeld)
        {
            return Failure<SubscriptionSeatResponse>(
                PaymentFailureKind.Conflict,
                "subscription_seat_already_held",
                "This person already holds a seat on this subscription.",
                correlationId);
        }

        // What the new holder may do has changed, and every subscriber in the organization caches
        // the organization's own subscription alongside their own seats.
        _cache.Invalidate(context.TenantId, subscription.OrganizationId);

        return SubscriptionOperationResult<SubscriptionSeatResponse>.Success(
            Describe(assignment), correlationId);
    }

    public async Task<SubscriptionOperationResult<SubscriptionSeatResponse>> ReleaseAsync(
        string subscriptionId,
        string userId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync<SubscriptionSeatResponse>(
            subscriptionId, correlationId, cancellationToken, requireLive: false);

        if (resolved.Failure is { } failure)
        {
            return failure;
        }

        var (context, subscription) = resolved.Value;
        var releasedAtUtc = _time.GetUtcNow().UtcDateTime;

        var outcome = await _assignments.TryReleaseAsync(
            context.TenantId, subscription.ItemId, userId, releasedAtUtc, cancellationToken);

        if (outcome == SeatReleaseOutcome.NotHeld)
        {
            return Failure<SubscriptionSeatResponse>(
                PaymentFailureKind.NotFound,
                "subscription_seat_not_held",
                "This person does not hold a seat on this subscription.",
                correlationId);
        }

        _cache.Invalidate(context.TenantId, subscription.OrganizationId);

        return SubscriptionOperationResult<SubscriptionSeatResponse>.Success(
            new SubscriptionSeatResponse
            {
                SubscriptionId = subscription.ItemId,
                UserId = userId,
                ReleasedAtUtc = releasedAtUtc
            },
            correlationId);
    }

    public async Task<SubscriptionOperationResult<SubscriptionSeatsResponse>> ListAsync(
        string subscriptionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync<SubscriptionSeatsResponse>(
            subscriptionId, correlationId, cancellationToken, requireLive: false);

        if (resolved.Failure is { } failure)
        {
            return failure;
        }

        var (context, subscription) = resolved.Value;

        var held = await _assignments.ListActiveAsync(
            context.TenantId, subscription.ItemId, cancellationToken);

        var purchased = PurchasedSeatsOf(subscription) ?? 0;

        return SubscriptionOperationResult<SubscriptionSeatsResponse>.Success(
            new SubscriptionSeatsResponse
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
    /// How many people this subscription paid to seat, or null when nothing says.
    /// </summary>
    /// <remarks>
    /// A plan may sell several quantities — seats, workspaces, whatever else — so the item that
    /// counts people is the one marked <see cref="SubscriptionQuantityItem.CountsSeats"/>. Nothing
    /// else on an item distinguishes them: <c>ItemKey</c> is a free string a product chose, and
    /// reading "seat" out of it would work for one tenant's naming and fail silently for another's.
    /// <para>
    /// A plan selling a single quantity needs no mark, which is what keeps every plan authored
    /// before this from needing an edit. A plan selling none sells one seat, because a user-wise
    /// subscription with nothing to count is a single person's plan.
    /// </para>
    /// <para>
    /// Several quantities and no mark is refused rather than guessed. Summing them would seat
    /// people against workspaces nobody sold as seats; taking the first would make the answer
    /// depend on authoring order; defaulting to one would quietly seat a single person on a
    /// subscription charged for eight. Each of those is wrong without anything failing, and the
    /// administrator finds out when the seventh person cannot work.
    /// </para>
    /// </remarks>
    private static long? PurchasedSeatsOf(SubscriptionDetail subscription)
    {
        if (subscription.QuantityItems.Count == 0)
        {
            return 1;
        }

        if (subscription.QuantityItems.Count == 1)
        {
            return subscription.QuantityItems[0].Quantity;
        }

        var seating = subscription.QuantityItems
            .Where(item => item.CountsSeats)
            .ToList();

        return seating.Count == 1 ? seating[0].Quantity : null;
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
                "subscription_not_seat_based",
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

    private static SubscriptionSeatResponse Describe(SubscriptionAssignment assignment) => new()
    {
        SubscriptionId = assignment.SubscriptionId,
        UserId = assignment.UserId,
        AssignedAtUtc = assignment.AssignedAtUtc,
        ReleasedAtUtc = assignment.ReleasedAtUtc
    };
}
