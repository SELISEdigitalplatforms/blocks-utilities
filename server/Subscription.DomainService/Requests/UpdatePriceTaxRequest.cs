using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Requests;

public sealed class UpdatePriceTaxRequest
{
    public string? OrganizationId { get; set; }

    public int? TaxRateBasisPoints { get; set; }

    public TaxMode? TaxMode { get; set; }

    /// <summary>
    /// Whether subscribers already on this price move to the new tax too, from their first renewal
    /// at least <c>Subscription:PriceTermsNoticeDays</c> away, each told by email. False by default:
    /// a misconfigured tax corrected for new sales must not reprice anybody already subscribed.
    /// </summary>
    public bool ApplyToExistingSubscribers { get; set; }
}
