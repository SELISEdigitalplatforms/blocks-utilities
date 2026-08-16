namespace Subscription.DomainService.Requests;

public sealed class CreatePlanRequest : PlanDefinitionRequest
{
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Scope the plan to one organization instead of the whole tenant. Omit for the ordinary
    /// case where the tenant sells the same plan to everyone.
    /// </summary>
    public string? OrganizationId { get; set; }
}
