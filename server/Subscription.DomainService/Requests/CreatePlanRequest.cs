namespace Subscription.DomainService.Requests;

public sealed class CreatePlanRequest
{
    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Arbitrary plan features as JSON. Stored verbatim and handed back untouched; nothing here
    /// interprets it, which is what lets a second product ship its own flags without a change
    /// to this module.
    /// </summary>
    public string? FeaturesJson { get; set; }

    /// <summary>
    /// Scope the plan to one organization instead of the whole tenant. Omit for the ordinary
    /// case where the tenant sells the same plan to everyone.
    /// </summary>
    public string? OrganizationId { get; set; }

    public int? TrialDays { get; set; }

    public bool TrialRequiresPaymentMethod { get; set; } = true;

    public List<PlanQuantityItemRequest> QuantityItems { get; set; } = [];

    public List<PlanMeterRequest> Meters { get; set; } = [];

    public List<PlanEntitlementRequest> Entitlements { get; set; } = [];

    public List<TrialGrantRequest> TrialGrants { get; set; } = [];
}
