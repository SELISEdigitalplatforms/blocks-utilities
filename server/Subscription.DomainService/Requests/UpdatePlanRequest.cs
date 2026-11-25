namespace Subscription.DomainService.Requests;

/// <summary>
/// Rewrites what a plan sells. Refused once anything has subscribed to it — see
/// <see cref="Services.IPlanCatalogueService.UpdatePlanAsync"/>.
/// </summary>
public sealed class UpdatePlanRequest : PlanDefinitionRequest
{
    /// <summary>
    /// The organization whose plan this is. Ignored unless the caller is the console
    /// (<c>Payment:ConsoleOrganizationId</c>) — everyone else edits their own organization's
    /// plans, whatever this says. It names the plan to find; it never moves the plan's scope.
    /// </summary>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Whether subscribers already on the plan get any changed credit terms too — prices, limits,
    /// expiry, tax, or pausing and ending sales — immediately, each billing contact the change reaches
    /// told by email. False by default: as with every other plan edit, existing subscribers keep the
    /// terms they signed up with. Only meters whose credit terms actually changed are pushed.
    /// </summary>
    public bool ApplyCreditTermsToExistingSubscribers { get; set; }
}
