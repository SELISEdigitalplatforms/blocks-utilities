using Payment.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Services;

/// <summary>
/// What <see cref="ISubscriptionPaymentProviderReadinessService.CheckAsync"/> found: not merely
/// whether a provider is ready, but -- when it is -- exactly which configuration answered.
/// </summary>
/// <remarks>
/// The provider matters as much as the verdict. Reporting readiness as a bare enum let checkout
/// validate one organization's configuration and then charge through whatever a separate,
/// independent resolution happened to find later -- which is not always the same row, because
/// scope resolution depends on which organization is asked. Carrying the actual
/// <see cref="Provider"/> here means every caller that asks "is this ready" and then "who do I
/// charge" asks the same question once, through the same lookup, and gets answers that cannot
/// disagree with each other.
/// </remarks>
/// <param name="Readiness">The verdict.</param>
/// <param name="Provider">
/// The configuration this evaluation resolved, when <paramref name="Readiness"/> is
/// <see cref="SubscriptionPaymentProviderReadiness.Ready"/>. Null otherwise -- there is nothing
/// to charge through if the provider is not ready, whatever partial match diagnosis found.
/// </param>
public sealed record SubscriptionPaymentProviderReadinessResult(
    SubscriptionPaymentProviderReadiness Readiness,
    PaymentProvider? Provider)
{
    public static SubscriptionPaymentProviderReadinessResult NotReady(
        SubscriptionPaymentProviderReadiness readiness)
    {
        if (readiness == SubscriptionPaymentProviderReadiness.Ready)
        {
            throw new ArgumentException(
                "Use the primary constructor with a resolved provider for Ready.",
                nameof(readiness));
        }

        return new SubscriptionPaymentProviderReadinessResult(readiness, null);
    }
}
