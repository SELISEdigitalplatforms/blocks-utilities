namespace Subscription.DomainService.Requests;

/// <summary>
/// Gives back a use recorded earlier, for a caller that books usage before an operation and the
/// operation then failed.
/// </summary>
public sealed class ReleaseUsageRequest
{
    /// <summary>The meter the use was recorded against.</summary>
    public string MeterKey { get; set; } = string.Empty;

    /// <summary>The idempotency key the use was recorded under.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>As on <see cref="RecordUsageRequest.OrganizationId"/>; must name the same organization.</summary>
    public string? OrganizationId { get; set; }

    /// <summary>As on <see cref="RecordUsageRequest.UserId"/>; must name the same user the use was recorded for.</summary>
    public string? UserId { get; set; }
}
