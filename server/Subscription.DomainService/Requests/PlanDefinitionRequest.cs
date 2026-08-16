namespace Subscription.DomainService.Requests;

/// <summary>
/// What a plan sells, shared by authoring one and editing one.
/// </summary>
/// <remarks>
/// Everything a plan is *made of* lives here; what it *is* — its code and the organization it is
/// scoped to — belongs to the request that creates it, because neither may change afterwards. A
/// code is what configuration points at, and a scope change would move a plan out from under the
/// organization that can see it.
/// </remarks>
public abstract class PlanDefinitionRequest
{
    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Arbitrary plan features as JSON. Stored verbatim and handed back untouched; nothing here
    /// interprets it, which is what lets a second product ship its own flags without a change
    /// to this module.
    /// </summary>
    public string? FeaturesJson { get; set; }

    public int? TrialDays { get; set; }

    public bool TrialRequiresPaymentMethod { get; set; } = true;

    public List<PlanQuantityItemRequest> QuantityItems { get; set; } = [];

    public List<PlanMeterRequest> Meters { get; set; } = [];

    public List<PlanEntitlementRequest> Entitlements { get; set; } = [];

    public List<TrialGrantRequest> TrialGrants { get; set; } = [];
}
