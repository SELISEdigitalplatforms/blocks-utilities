using Payment.DomainService.Utilities;

namespace Subscription.DomainService.Simulation;

/// <summary>
/// Whether a caller's own organization may reach the subscription simulation harness at all.
/// </summary>
/// <remarks>
/// Pure and synchronous, mirroring <see cref="PaymentOrganizationScope"/> — the same reasoning
/// applies: this is checked before any repository round trip, not after one.
/// <para>
/// Scope only. Whether the caller carries the permission the harness requires is not decided
/// here and never was: every simulation endpoint is a
/// <c>[ProtectedEndPoint("blocks-utilities::subscription-simulation::*")]</c>, so the framework has already refused
/// a caller without it before any of this runs. What remains is the part the framework cannot
/// know — that the harness must never be reachable by a wider audience than the platform-console
/// override already is, so that an ordinary organization's own token can never unlock this
/// surface for its own subscription. That check reuses
/// <see cref="PaymentOrganizationScope.RequestMayNameOrganization"/> rather than duplicating it.
/// </para>
/// </remarks>
public static class SubscriptionSimulationGuard
{
    /// <summary>True only for a caller whose own token is scoped to the platform console.</summary>
    /// <param name="callerOrganizationId">The organization from the caller's own token — never
    /// the organization a request names, which is the very thing this decides whether to trust.</param>
    /// <param name="paymentOptions">Supplies the console's organization identifier.</param>
    public static bool IsAuthorized(
        string? callerOrganizationId,
        PaymentOptions paymentOptions)
    {
        ArgumentNullException.ThrowIfNull(paymentOptions);

        return PaymentOrganizationScope.RequestMayNameOrganization(callerOrganizationId, paymentOptions);
    }
}
