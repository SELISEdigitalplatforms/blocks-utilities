namespace Subscription.DomainService.Responses;

public sealed class MeterCatalogueEntryResponse
{
    public string EntryId { get; init; } = string.Empty;

    /// <summary>Null for the tenant-wide entry; otherwise the organization it overrides for.</summary>
    public string? OrganizationId { get; init; }

    public string MeterKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string UnitLabel { get; init; } = string.Empty;

    public string Aggregation { get; init; } = string.Empty;

    public int QuantityScale { get; init; }

    public string Status { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public DateTime LastUpdatedAtUtc { get; init; }

    public long Version { get; init; }
}

public sealed class CreditOfferResponse
{
    public string OfferId { get; init; } = string.Empty;

    public string? OrganizationId { get; init; }

    public string MeterKey { get; init; } = string.Empty;

    /// <summary>The catalogue meter's name and unit, for a buyer who has never seen the key.</summary>
    public string DisplayName { get; init; } = string.Empty;

    public string UnitLabel { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public List<PlanMeterTopUpPriceResponse> UnitPrices { get; init; } = [];

    public decimal MinQuantity { get; init; }

    public decimal MaxQuantity { get; init; }

    public CreditExpiryDurationResponse? ExpiresAfter { get; init; }

    public int? TaxRateBasisPoints { get; init; }

    public string? TaxMode { get; init; }

    public string SpendOrder { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public DateTime LastUpdatedAtUtc { get; init; }

    public long Version { get; init; }
}
