namespace Subscription.DomainService.Requests;

/// <summary>
/// An operator moving an offline subscription onto another plan, with the pricing it moves on.
/// See spec 006.
/// </summary>
/// <remarks>
/// The organization's own plan-change route always lands on the target's catalogue price; this one
/// lets the operator name a contract price for the new plan, or keep catalogue pricing explicitly.
/// </remarks>
public sealed class OfflinePlanChangeRequest
{
    /// <summary>The customer organization. Required: an operator always acts for somebody else.</summary>
    public string OrganizationId { get; set; } = string.Empty;

    public string PlanCode { get; set; } = string.Empty;

    public string PriceId { get; set; } = string.Empty;

    /// <summary>Defaults to the target plan's own defaults for anything left out, same as signup.</summary>
    public IReadOnlyList<SubscriptionQuantityRequest> Quantities { get; init; } = [];

    public OfflinePricingRequest Pricing { get; set; } = new();

    /// <summary>The ordinary change these terms correspond to, so its rules decide them.</summary>
    public ChangeSubscriptionPlanRequest ToChangeRequest() => new()
    {
        OrganizationId = OrganizationId,
        PlanCode = PlanCode,
        PriceId = PriceId,
        Quantities = [.. Quantities]
    };
}

/// <summary>
/// An operator re-pricing an offline subscription from its next renewal. See spec 006.
/// </summary>
/// <remarks>
/// The plan, price cadence and quantities stay as they are; only what the period is invoiced at
/// changes, and only from the boundary the customer has not yet been invoiced for.
/// </remarks>
public sealed class OfflineRepricingRequest
{
    /// <summary>The customer organization. Required: an operator always acts for somebody else.</summary>
    public string OrganizationId { get; set; } = string.Empty;

    public OfflinePricingRequest Pricing { get; set; } = new();
}
