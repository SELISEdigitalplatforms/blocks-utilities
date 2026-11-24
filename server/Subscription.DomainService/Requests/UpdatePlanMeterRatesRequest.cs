namespace Subscription.DomainService.Requests;

/// <summary>
/// Rewrites one meter's overage rate tables, leaving the rest of the plan alone.
/// </summary>
/// <remarks>
/// The deliberate exception to <see cref="UpdatePlanRequest"/>'s subscriber lock, alongside the
/// price tax and discount editors: a subscription rates its overage from the meter snapshot
/// copied onto it at signup and never reads the catalogue again, so by default this reaches only
/// future subscriptions and future plan changes onto this plan. Existing subscribers are repriced
/// only when <see cref="ApplyToExistingSubscribers"/> says so.
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

    /// <summary>
    /// Whether subscribers already on this plan get the new rates too — immediately, so the
    /// billing period still open is rated at them when it closes, each billing contact told by
    /// email. False by default: unlike a tax or discount edit there is no notice period, so a rate
    /// increase extended this way bills usage already consumed at the higher rate.
    /// </summary>
    public bool ApplyToExistingSubscribers { get; set; }
}
