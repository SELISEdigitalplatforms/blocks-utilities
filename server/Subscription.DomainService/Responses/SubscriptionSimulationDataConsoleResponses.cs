namespace Subscription.DomainService.Responses;

/// <summary>The allowlisted collections and what the console may do to each — for discoverability.</summary>
public sealed class SubscriptionSimulationDataPolicyResponse
{
    public string LogicalName { get; init; } = string.Empty;

    public bool CanRead { get; init; }

    public bool CanInsert { get; init; }

    public List<string> UpdatableFields { get; init; } = [];
}

public sealed class SubscriptionSimulationDataQueryResponse
{
    public string Collection { get; init; } = string.Empty;

    public int Count { get; init; }

    /// <summary>Each document as JSON, with the same redaction the state endpoint already applies.</summary>
    public List<string> Documents { get; init; } = [];

    public string CorrelationId { get; init; } = string.Empty;
}

public sealed class SubscriptionSimulationDataMutationResponse
{
    public string Collection { get; init; } = string.Empty;

    public bool Modified { get; init; }

    public List<string> FieldsSet { get; init; } = [];

    public string CorrelationId { get; init; } = string.Empty;
}
