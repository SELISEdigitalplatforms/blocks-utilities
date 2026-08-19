using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

[BsonIgnoreExtraElements]
public sealed class Discount
{
    [BsonId] public string ItemId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string? OrganizationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public CatalogueStatus Status { get; set; } = CatalogueStatus.Active;
    public DiscountTerms Terms { get; set; } = new();
    public string? CurrencyCode { get; set; }
    public List<string> ApplicablePlanCodes { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;
}
