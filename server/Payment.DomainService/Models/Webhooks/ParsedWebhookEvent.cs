using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Models.Webhooks;

/// <summary>
/// One inbound event after its provider's normalizer has read it, but before the system has
/// resolved which payment it belongs to.
/// </summary>
/// <remarks>
/// <see cref="Payload"/> arrives fully populated except for the routing identifiers
/// (payment, refund and capture ids), which intake fills in once it has verified the event
/// really does own the payment it names.
/// </remarks>
public sealed class ParsedWebhookEvent
{
    /// <summary>Provider's own kind for this webhook, retained for diagnostics and dedup scoping.</summary>
    public required string WebhookType { get; init; }

    /// <summary>Provider's own event name, retained for logging and inbox records.</summary>
    public required string EventCode { get; init; }

    public required WebhookIntent Intent { get; init; }

    public required DateTime EventDateUtc { get; init; }

    /// <summary>
    /// The reference this service minted when it started the payment, echoed back by the
    /// provider. Routing decodes the tenant and payment from it.
    /// </summary>
    public required string RoutingReference { get; init; }

    public required WebhookSignature Signature { get; init; }

    public required PaymentWebhookPayload Payload { get; init; }

    /// <summary>Seed for the inbox deduplication key; intake scopes it by tenant and hashes it.</summary>
    public required string DeduplicationSeed { get; init; }

    /// <summary>Provider's identifier for this event, used for correlation in logs.</summary>
    public string? ProviderEventId { get; init; }

    /// <summary>
    /// Tenant identifier the provider echoed back in its own metadata, when it carries one.
    /// Checked against the routed tenant as defence in depth.
    /// </summary>
    public string? EchoedTenantId { get; init; }

    /// <summary>
    /// Organization identifier the provider echoed back in its own metadata, when it carries
    /// one. Null for an event raised against an object this service did not label.
    /// </summary>
    /// <remarks>
    /// Two organizations under one tenant may pay through different merchant accounts, so the
    /// configuration that verifies an event's signature depends on which one it belongs to.
    /// Intake must choose that configuration before it can trust anything — including the
    /// payment record — so the organization has to arrive on the event itself. It is checked
    /// against the payment once loaded, exactly as the echoed tenant is.
    /// </remarks>
    public string? EchoedOrganizationId { get; init; }
}
