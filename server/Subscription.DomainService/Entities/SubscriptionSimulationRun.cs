using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One call into the subscription simulation harness, kept separately from
/// <see cref="SubscriptionAuditEvent"/>.
/// </summary>
/// <remarks>
/// A simulation run is a record that a tester or an administrator drove the domain through a
/// scenario, not that the domain itself did something — the business audit trail still gets its
/// own <see cref="SubscriptionAuditEvent"/> for the actual operation performed, because that
/// operation genuinely happened and must appear in the same trail a real caller's would. Mixing
/// the two would make it impossible to later purge or restrict simulation history without
/// touching real financial audit records, or to tell, from the business trail alone, that an
/// action was real.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionSimulationRun
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString("N");

    public string TenantId { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;

    public string? SubscriptionId { get; set; }

    /// <summary>The caller who ran the simulation, from the authenticated context — never null.</summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>E.g. <c>InspectState</c>, <c>AdvanceRenewal</c>, <c>MarkPaymentSucceeded</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>What was asked for, as a short human-readable line — not the full request body.</summary>
    public string? RequestSummary { get; set; }

    public string? BeforeSummary { get; set; }

    public string? AfterSummary { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string? ErrorCode { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }
}
