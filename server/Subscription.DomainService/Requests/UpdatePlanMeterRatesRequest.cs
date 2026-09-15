namespace Subscription.DomainService.Requests;

/// <summary>
/// Rewrites one meter's overage rate tables, leaving the rest of the plan alone.
/// </summary>
/// <remarks>
/// The deliberate exception to <see cref="UpdatePlanRequest"/>'s subscriber lock, alongside the
/// price tax and discount editors: a subscription rates its overage from the meter snapshot
/// copied onto it at signup and never reads the catalogue again, so this reaches only future
/// subscriptions and future renewals onto this plan. Nobody already subscribed is repriced by
/// it, whether or not anyone else has ever subscribed to this plan.
/// </remarks>
public sealed class UpdatePlanMeterRatesRequest
{
    /// <summary>
    /// The organization whose plan this is. Ignored unless the caller is the console — everyone
    /// else edits their own organization's plans, whatever this says.
    /// </summary>
    public string? OrganizationId { get; set; }

    /// <summary>The meter's rate tables in full — this replaces the set, it does not merge it.</summary>
    public List<MeterRateTableRequest> RateTables { get; set; } = [];
}
