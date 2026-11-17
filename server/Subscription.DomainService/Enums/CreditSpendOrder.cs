using System.Text.Json.Serialization;

namespace Subscription.DomainService.Enums;

/// <summary>
/// Which credits a recording spends first when an organization holds standalone credits for a
/// meter and also has a subscription that meters it. See spec 005.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CreditSpendOrder
{
    /// <summary>Plan allowance, then the subscription's own grants, then the organization's credits.</summary>
    SubscriptionFirst = 0,

    /// <summary>The organization's credits before anything the subscription provides.</summary>
    OrganizationCreditsFirst = 1
}
