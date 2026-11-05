using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Requests;

/// <summary>A new catalogue meter: identity and counting semantics only. See spec 005.</summary>
public sealed class CreateMeterCatalogueEntryRequest
{
    /// <summary>
    /// The organization whose override this is. Honoured only for the platform console; omitted,
    /// the console authors the tenant-wide entry. Everyone else authors for their own organization.
    /// </summary>
    public string? OrganizationId { get; set; }

    public string MeterKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string UnitLabel { get; set; } = string.Empty;

    public MeterAggregation Aggregation { get; set; } = MeterAggregation.Sum;

    public int QuantityScale { get; set; }
}

/// <summary>Everything about a catalogue meter but its key, which never changes.</summary>
public sealed class UpdateMeterCatalogueEntryRequest
{
    public string DisplayName { get; set; } = string.Empty;

    public string UnitLabel { get; set; } = string.Empty;

    public MeterAggregation Aggregation { get; set; } = MeterAggregation.Sum;

    public int QuantityScale { get; set; }

    /// <summary>Active or Archived. Archiving refuses new offers on the meter; plans and offers already on it keep working.</summary>
    public CatalogueStatus Status { get; set; } = CatalogueStatus.Active;
}
