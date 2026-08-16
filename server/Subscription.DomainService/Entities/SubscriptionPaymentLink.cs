using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// Ties a payment to the subscription it was raised for, and tracks whether its outcome has
/// been applied.
/// </summary>
/// <remarks>
/// A separate record rather than a field on either side, because the two are written by
/// different processes at different times and the link is what a sweep can scan for.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionPaymentLink
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public string PaymentDetailId { get; set; } = string.Empty;

    public string OrderId { get; set; } = string.Empty;

    public SubscriptionPaymentPurpose Purpose { get; set; } =
        SubscriptionPaymentPurpose.InitialCharge;

    public SubscriptionPaymentLinkState State { get; set; } =
        SubscriptionPaymentLinkState.Pending;

    public int AttemptCount { get; set; }

    /// <summary>When the sweep should look at this link again.</summary>
    public DateTime? NextCheckAtUtc { get; set; }

    public string? LastError { get; set; }

    /// <summary>Carried so the sweep's work can be traced back to the request that started it.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;
}
