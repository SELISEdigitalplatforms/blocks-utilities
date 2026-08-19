namespace Subscription.DomainService.Requests;

public sealed class GetSubscriptionInvoicesRequest
{
    public int PageSize { get; set; } = 25;

    public string? After { get; set; }

    /// <summary>
    /// Honoured only for the platform console, using the standard subscription organization
    /// resolution policy.
    /// </summary>
    public string? OrganizationId { get; set; }
}
