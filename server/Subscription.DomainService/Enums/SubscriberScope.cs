namespace Subscription.DomainService.Enums;

/// <summary>
/// Who a plan is sold to, and therefore who holds the subscription that buys it.
/// </summary>
/// <remarks>
/// <see cref="Organization"/> is what every plan authored before this existed already meant, so an
/// absent value deserializing to it is correct rather than merely convenient.
/// <para>
/// The two are not alternatives an organization picks between. An organization-scoped plan covers
/// what the organization shares and a user-scoped plan covers one person's own allowance, so an
/// organization commonly holds both at once and a subscriber draws on whichever one declares the
/// entitlement being asked about.
/// </para>
/// </remarks>
public enum SubscriberScope
{
    Organization = 0,
    User = 1
}
