using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A plan exactly as it stood at one version, kept after a later write moved it on.
/// </summary>
/// <remarks>
/// A plan stays editable after it is sold because nobody already on it reads the catalogue
/// again: each subscription bills from its own <see cref="PlanSnapshot"/>. What that loses is
/// the catalogue's own memory — a support question about what v1 offered could otherwise only
/// be answered by finding a subscriber who happened to buy it. So every write that moves
/// <see cref="Plan.Version"/> on first copies the version it is replacing here.
/// <para>
/// Written once and never updated. The id is the plan and version together, so copying the same
/// version twice — a retried request, two edits racing from the same read — is refused by the
/// key rather than recorded twice, and the first copy stands.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PlanVersionRecord
{
    [BsonId]
    public string ItemId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string PlanId { get; set; } = string.Empty;

    public int Version { get; set; }

    /// <summary>When the write that replaced this version was made.</summary>
    public DateTime SupersededAtUtc { get; set; }

    public Plan Plan { get; set; } = new();

    public static string IdOf(string planId, int version) => $"{planId}:{version}";
}
