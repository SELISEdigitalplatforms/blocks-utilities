using Subscription.DomainService.Enums;

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
    /// <summary>
    /// Who this plan is sold to. Defaults to the organization, which is what every plan authored
    /// before this existed is.
    /// </summary>
    /// <remarks>
    /// Named on creation and never afterwards, for the reason <see cref="Code"/> and
    /// <see cref="OrganizationId"/> are: it decides which reservation slot a subscription sold on
    /// this plan occupies, and moving a plan between scopes would leave subscriptions already sold
    /// under the other one. An edit carries the stored value forward rather than reading it from
    /// the request.
    /// <para>
    /// Publishing a <see cref="SubscriberScope.User"/> plan into an organization's catalogue is how
    /// user-wise subscriptions become available to it; archiving that plan withdraws the offer
    /// without disturbing the people already holding seats on what they bought.
    /// </para>
    /// </remarks>
    public SubscriberScope SubscriberScope { get; set; } = SubscriberScope.Organization;

    public string? PredecessorPlanId { get; set; }
}
