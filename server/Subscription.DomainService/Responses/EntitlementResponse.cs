namespace Subscription.DomainService.Responses;

/// <summary>
/// What an organization may do right now.
/// </summary>
public sealed class EntitlementSnapshotResponse
{
    public bool HasSubscription { get; init; }

    public string Status { get; init; } = string.Empty;

    public string PlanCode { get; init; } = string.Empty;

    public DateTime? CurrentPeriodEndUtc { get; init; }

    public DateTime? TrialEndsAtUtc { get; init; }

    public List<EntitlementQuantityResponse> Quantities { get; init; } = [];

    public List<EntitlementResponse> Entitlements { get; init; } = [];

    /// <summary>
    /// The plan's own feature bag, verbatim. Stored, versioned and served; never interpreted.
    /// This is what lets one product ship flags another has never heard of.
    /// </summary>
    public string? FeaturesJson { get; init; }
}

public sealed class EntitlementQuantityResponse
{
    public string ItemKey { get; init; } = string.Empty;

    /// <summary>The product's own word — "seat", "user", "workspace".</summary>
    public string UnitLabel { get; init; } = string.Empty;

    public long Quantity { get; init; }
}

public sealed class EntitlementResponse
{
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Whether it is granted. Advisory: for anything metered, the authoritative answer is the
    /// balance returned by recording the usage, which cannot be beaten by a second caller.
    /// </summary>
    public bool Allowed { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string LimitKind { get; init; } = string.Empty;

    public long? Limit { get; init; }

    public long? Used { get; init; }

    public long? Remaining { get; init; }

    public string? UnitLabel { get; init; }
}
