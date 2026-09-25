using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

/// <summary>
/// What a caller may draw on: the subscriptions they hold a seat on, and their organization's own.
/// </summary>
/// <remarks>
/// One place, because every caller that asks the question has to answer it identically. Entitlement
/// asking "what may this person do" and usage asking "whose allowance does this consume" that
/// disagreed would let somebody be told they may act and then have the act counted against a plan
/// they are not on.
/// </remarks>
public interface ISubscriberSubscriptionResolver
{
    /// <summary>
    /// Everything currently granting something to this caller, their own seats first.
    /// </summary>
    /// <remarks>
    /// Both kinds, never one or the other. An organization-wise plan covers what the organization
    /// shares and a seat covers one person's own allowance, so returning only the seats would
    /// revoke everything shared the moment somebody was given one.
    /// <para>
    /// Seats first, because that is the precedence to apply where both declare the same thing: the
    /// more specific purchase answers. A caller with no user holds no seats and resolves the
    /// organization's alone.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<SubscriptionDetail>> ResolveAsync(
        SubscriptionContext context,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}
