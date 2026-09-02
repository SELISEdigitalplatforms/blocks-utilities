namespace Subscription.DomainService.Requests;

/// <summary>
/// A hypothetical slice of extra metered usage to price, without recording anything.
/// </summary>
public sealed class PreviewUsageOverageRequest
{
    /// <summary>
    /// Which organization's subscription to preview. Omit it to use the caller's own organization.
    /// </summary>
    /// <remarks>
    /// Honoured only for the platform console — see
    /// <see cref="Subscription.DomainService.Services.ISubscriptionContextResolver"/> for the full
    /// rule, the same one <c>GET /subscription-usage/current</c> already applies.
    /// </remarks>
    public string? OrganizationId { get; set; }

    /// <summary>The meter's key, in the calling product's own vocabulary.</summary>
    public string MeterKey { get; set; } = string.Empty;

    /// <summary>
    /// How much more usage to price, on top of what has already been recorded this period. Cannot
    /// be zero or negative — a preview of no additional usage is not a question this endpoint
    /// answers.
    /// </summary>
    public decimal AdditionalQuantity { get; set; }
}
