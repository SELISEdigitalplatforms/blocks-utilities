namespace Subscription.DomainService.Simulation;

/// <summary>
/// Sets one or more fields on exactly one document, restricted to the fields the target
/// collection's <see cref="SimulationCollectionPolicy"/> allows.
/// </summary>
/// <remarks>
/// Every value is parsed as a UTC timestamp — the only type any currently allowlisted field
/// holds — so there is no generic "any BSON value" write surface. A field this cannot parse, or
/// that the policy does not name, is rejected rather than silently ignored.
/// </remarks>
public sealed class UpdateDataFieldRequest
{
    public string? OrganizationId { get; set; }

    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>Field name to an ISO 8601 UTC timestamp string.</summary>
    public Dictionary<string, string> Fields { get; set; } = [];
}
