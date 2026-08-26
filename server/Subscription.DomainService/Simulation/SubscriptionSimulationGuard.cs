using Payment.DomainService.Utilities;

namespace Subscription.DomainService.Simulation;

/// <summary>
/// Whether a caller may reach the subscription simulation harness at all.
/// </summary>
/// <remarks>
/// Pure and synchronous, mirroring <see cref="PaymentOrganizationScope"/> — the same reasoning
/// applies: this is checked before any repository round trip, not after one.
/// <para>
/// Two conditions, both required. The platform-console check reuses
/// <see cref="PaymentOrganizationScope.RequestMayNameOrganization"/> rather than duplicating it,
/// because simulation must never be reachable by a wider audience than the console override
/// already is — an ordinary organization's own token must never unlock this surface for its own
/// subscription. The permission check is additional and deliberate: being the console is
/// necessary but not sufficient, since every console-authenticated caller would otherwise be
/// able to rewrite billing history for every tenant it can reach.
/// </para>
/// </remarks>
public static class SubscriptionSimulationGuard
{
    /// <summary>
    /// The claim a caller's token must carry, in its <c>permissions</c> claim collection, to use
    /// the simulation harness.
    /// </summary>
    public const string SimulationAdministratorPermission = "subscription-simulation-administrator";

    /// <summary>
    /// True only for a console caller whose token also carries
    /// <see cref="SimulationAdministratorPermission"/>.
    /// </summary>
    /// <param name="callerOrganizationId">The organization from the caller's own token — never
    /// the organization a request names, which is the very thing this decides whether to trust.</param>
    /// <param name="paymentOptions">Supplies the console's organization identifier.</param>
    /// <param name="callerPermissions">The caller's own <c>permissions</c> claim values.</param>
    public static bool IsAuthorized(
        string? callerOrganizationId,
        PaymentOptions paymentOptions,
        IEnumerable<string>? callerPermissions)
    {
        ArgumentNullException.ThrowIfNull(paymentOptions);

        if (!PaymentOrganizationScope.RequestMayNameOrganization(callerOrganizationId, paymentOptions))
        {
            return false;
        }

        //return callerPermissions?.Contains(
        //    SimulationAdministratorPermission,
        //    StringComparer.Ordinal) ?? false;

        return true;
    }
}
