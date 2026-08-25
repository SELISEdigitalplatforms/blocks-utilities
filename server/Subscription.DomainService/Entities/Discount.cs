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

    /// <summary>
    /// The prices this code may be used on. Empty is unrestricted by price, which is what every
    /// discount stored before this existed carries.
    /// </summary>
    /// <remarks>
    /// Price identifiers rather than cadences, because a plan can sell two yearly prices in two
    /// currencies and a promotion often means exactly one of them. Combined with
    /// <see cref="ApplicablePlanCodes"/> by <em>and</em>: naming both narrows twice rather than
    /// offering two ways to qualify.
    /// </remarks>
    public List<string> ApplicablePriceIds { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;
}
