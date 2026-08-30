namespace Subscription.DomainService.Responses;

/// <summary>A buyer-safe discount answer: rejection is data, while the ordinary quote remains usable.</summary>
public sealed class SubscriptionDiscountPreviewResponse
{
    public string Status { get; init; } = string.Empty;
    public string? ReasonCode { get; init; }
    public string? Message { get; init; }
    public SubscriptionPreviewResponse Quote { get; init; } = new();
}
