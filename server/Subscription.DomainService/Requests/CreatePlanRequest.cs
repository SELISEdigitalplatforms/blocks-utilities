namespace Subscription.DomainService.Requests;

public sealed class CreatePlanRequest : PlanDefinitionRequest
{
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Scope the plan to one organization instead of the whole tenant. Omit for the ordinary
    /// case where the tenant sells the same plan to everyone.
    /// </summary>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// The plan this one replaces, for display only. Naming one here does not migrate any
    /// subscriber and does not affect either plan's editability or purchasability — see
    /// <see cref="Subscription.DomainService.Entities.Plan.PredecessorPlanId"/>.
    /// </summary>
    public string? PredecessorPlanId { get; set; }
}
