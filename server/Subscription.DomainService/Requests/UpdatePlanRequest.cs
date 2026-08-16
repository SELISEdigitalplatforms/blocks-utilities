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
}
