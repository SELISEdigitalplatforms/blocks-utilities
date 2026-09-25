using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;

namespace Subscription.DomainService.Services;

/// <summary>
/// Resolves what a caller may draw on, from the seats they hold and their organization's own plan.
/// </summary>
public sealed class SubscriberSubscriptionResolver : ISubscriberSubscriptionResolver
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionAssignmentRepository _assignments;

    public SubscriberSubscriptionResolver(
        ISubscriptionRepository subscriptions,
        ISubscriptionAssignmentRepository assignments)
    {
        _subscriptions = subscriptions;
        _assignments = assignments;
    }

    public async Task<IReadOnlyList<ResolvedSubscription>> ResolveAsync(
        SubscriptionContext context,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var subscriberUserId = context.UserId ?? string.Empty;

        var seats = subscriberUserId.Length == 0
            ? []
            : await _assignments.ListSeatsForUserAsync(
                context.TenantId,
                context.OrganizationId,
                subscriberUserId,
                cancellationToken);

        var live = await _subscriptions.ListLiveByIdsAsync(
            context.TenantId,
            [.. seats.Select(seat => seat.SubscriptionId)],
            nowUtc,
            cancellationToken);

        // Paired back up by identifier rather than by position: the repository answers a set and
        // is under no obligation to return it in the order asked, and a seat number attached to
        // the wrong subscription would spend a different person's allowance.
        var seatBySubscription = seats.ToDictionary(
            seat => seat.SubscriptionId,
            seat => seat.SeatNumber,
            StringComparer.Ordinal);

        var held = live
            .Select(subscription => new ResolvedSubscription(
                subscription,
                seatBySubscription.TryGetValue(subscription.ItemId, out var seat) ? seat : null))
            .ToList();

        var organization = await _subscriptions.GetLiveAsync(
            context.TenantId, context.OrganizationId, nowUtc, cancellationToken);

        // The guard is for a subscription reached both ways, which cannot happen while an
        // organization-wise subscription carries no seats — but a duplicate would double a plan's
        // entitlements wherever a reader merges them, so it costs nothing to refuse it here.
        if (organization is null ||
            held.Any(candidate => string.Equals(
                candidate.Subscription.ItemId, organization.ItemId, StringComparison.Ordinal)))
        {
            return held;
        }

        return [.. held, new ResolvedSubscription(organization, SeatNumber: null)];
    }
}

/// <summary>
/// Picking one of the resolved subscriptions by what is being asked of it.
/// </summary>
public static class SubscriberSubscriptionSelection
{
    /// <summary>
    /// The subscription whose plan meters this key, or null when none of them does.
    /// </summary>
    /// <remarks>
    /// First match wins, and the order is what carries the precedence: a seat the caller holds is
    /// consulted before the organization's own plan, so a person given their own allowance spends
    /// theirs rather than everybody's.
    /// <para>
    /// Selected by meter rather than by taking the first subscription outright, because the two
    /// plans cover different things. A person on an allowance plan still records against the
    /// organization's plan for a meter their own says nothing about — the alternative silently
    /// refuses usage the organization is paying for.
    /// </para>
    /// </remarks>
    public static ResolvedSubscription? ForMeter(
        IReadOnlyList<ResolvedSubscription> resolved,
        string meterKey)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        return resolved.FirstOrDefault(candidate =>
            candidate.Subscription.Plan.Meters.Exists(meter =>
                string.Equals(meter.MeterKey, meterKey, StringComparison.Ordinal)));
    }
}
