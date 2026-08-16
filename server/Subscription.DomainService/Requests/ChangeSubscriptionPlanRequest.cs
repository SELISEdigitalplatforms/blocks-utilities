namespace Subscription.DomainService.Requests;

public sealed class ChangeSubscriptionPlanRequest
{
    public string PlanCode { get; set; } = string.Empty;

    public string PriceId { get; set; } = string.Empty;

    /// <summary>Defaults to the target plan's own defaults for anything left out, same as signup.</summary>
    public List<SubscriptionQuantityRequest> Quantities { get; set; } = [];

    /// <summary>
    /// Which organization's subscription to change. Omit it to use the caller's own
    /// organization.
    /// </summary>
    /// <remarks>
    /// Ignored unless the caller is the platform console — see
    /// <see cref="CreateSubscriptionRequest.OrganizationId"/> for the full rule.
    /// </remarks>
    public string? OrganizationId { get; set; }
}
