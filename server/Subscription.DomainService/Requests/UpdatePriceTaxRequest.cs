using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Requests;

public sealed class UpdatePriceTaxRequest
{
    public string? OrganizationId { get; set; }

    public int? TaxRateBasisPoints { get; set; }

    public TaxMode? TaxMode { get; set; }
}
