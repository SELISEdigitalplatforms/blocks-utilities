namespace Subscription.DomainService.Requests;

public sealed class RecordUsageRequest
{
    /// <summary>The meter's key, in the calling product's own vocabulary.</summary>
    public string MeterKey { get; set; } = string.Empty;

    /// <summary>
    /// How much was used. A negative value releases capacity only on a Never-reset meter.
    /// </summary>
    public decimal Quantity { get; set; } = 1;

    /// <summary>
    /// Unique per subscription and meter. Mandatory, because at-least-once delivery makes a
    /// repeated call a certainty rather than a risk, and the second one must not be billable.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// When it happened, if that is not now. Decides which period it lands in, so a late report
    /// still bills against the month it belongs to.
    /// </summary>
    public DateTime? OccurredAtUtc { get; set; }

    /// <summary>
    /// Refuse and roll back when the allowance is exhausted, instead of recording overage.
    /// </summary>
    /// <remarks>
    /// This is the only way to actually gate on a limit. Reading the entitlement first and
    /// deciding is a check, not enforcement: two callers at 99 of 100 both pass it, and only
    /// the increment can tell them apart.
    /// </remarks>
    public bool Enforce { get; set; }

    /// <summary>
    /// Free-form context, bounded in count and length.
    /// </summary>
    /// <remarks>
    /// Billing needs a count, not a dossier. Anything identifying a person belongs in the
    /// calling product's own records, not in a shared billing ledger that is retained for years
    /// and exported for invoicing.
    /// </remarks>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Which organization's subscription this usage counts against. Omit it to use the
    /// caller's own organization.
    /// </summary>
    /// <remarks>
    /// Ignored unless the caller is the platform console — see
    /// <see cref="CreateSubscriptionRequest.OrganizationId"/> for the full rule.
    /// </remarks>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// The person whose place this usage spends, for a caller that is not a person itself.
    /// </summary>
    /// <remarks>
    /// Read only when the token names no user — a client-credentials caller, which reports usage on
    /// somebody's behalf and has no other way to say whose. A token that names a user is the
    /// stronger claim and this is ignored, the same rule <see cref="OrganizationId"/> follows, so a
    /// signed-in caller cannot spend a colleague's allowance by naming them.
    /// <para>
    /// A named user who holds a place on a plan sold per person that meters this key spends that
    /// place, and the ledger entry is theirs. One who holds none is refused when such a plan exists,
    /// rather than charged to the organization: the caller asked for this person, and billing the
    /// shared pool instead would hide a mistyped id as a successful call. When only the
    /// organization's own plan meters the key, the usage is the organization's, as it would be
    /// without a user named.
    /// </para>
    /// </remarks>
    public string? UserId { get; set; }
}
