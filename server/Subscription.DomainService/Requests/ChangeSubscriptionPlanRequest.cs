namespace Subscription.DomainService.Requests;

public sealed class ChangeSubscriptionPlanRequest
{
    public string PlanCode { get; set; } = string.Empty;

    public string PriceId { get; set; } = string.Empty;

    /// <summary>Defaults to the target plan's own defaults for anything left out, same as signup.</summary>
    public List<SubscriptionQuantityRequest> Quantities { get; set; } = [];
}
