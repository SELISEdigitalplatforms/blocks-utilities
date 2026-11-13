namespace Subscription.DomainService.Entities;

/// <summary>
/// What is published to the lifecycle topic when something happens to a subscription.
/// </summary>
/// <remarks>
/// The platform states the fact; each product decides what it means. A quota alert is an event
/// here rather than an email because what counts as "warn the customer" differs per product,
/// and this service has no business owning that decision — or a mail server.
/// </remarks>
public sealed class SubscriptionLifecycleEvent
{
    public string EventId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public int SchemaVersion { get; set; } = 1;

    public string TenantId { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public string PlanCode { get; set; } = string.Empty;

    /// <summary>Set only on a plan-change event; <see cref="PlanCode"/> already carries the new one.</summary>
    public string? PreviousPlanCode { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Whether the subscription is running out a scheduled cancellation rather than having already
    /// ended.
    /// </summary>
    /// <remarks>
    /// Carried on every lifecycle event because <see cref="Status"/> alone cannot express it: a
    /// subscriber who cancels keeps what they paid for, so the status stays <c>Active</c> until the
    /// boundary passes. A consumer told only that a cancellation was requested, with no way to see
    /// this pair, has nothing to do but revoke on receipt — which takes access away on the day
    /// someone cancels, the very thing this module refuses to do.
    /// </remarks>
    public bool CancelAtPeriodEnd { get; set; }

    /// <summary>
    /// The instant entitlement actually stops — the paid period's end for a scheduled cancellation,
    /// the moment it ended for one that has taken effect. Null only where the event says nothing
    /// about a boundary.
    /// </summary>
    public DateTime? CurrentPeriodEndUtc { get; set; }

    /// <summary>Set on usage events; absent on lifecycle transitions.</summary>
    public string? MeterKey { get; set; }

    public long? ThresholdPercent { get; set; }

    public decimal? Balance { get; set; }

    public decimal? Limit { get; set; }

    /// <summary>Set on renewal and dunning events; absent otherwise.</summary>
    public string? PeriodKey { get; set; }

    /// <summary>The dunning attempt this event resulted from, 1-based.</summary>
    public int? AttemptNumber { get; set; }

    /// <summary>Set on a plan-change event: the name of <see cref="PreviousPlanCode"/>.</summary>
    /// <remarks>
    /// Carried rather than looked up when the mail is sent, because the plan may have been renamed
    /// by then and the customer moved off the plan as it was called on the day.
    /// </remarks>
    public string? PreviousPlanName { get; set; }

    /// <summary>The plan's display name, for the notification emails.</summary>
    public string? PlanName { get; set; }

    /// <summary>Set on a quantity-change event: every item whose quantity moved.</summary>
    public List<LifecycleQuantityChange>? QuantityChanges { get; set; }

    /// <summary>Set on cancellation events: the reason the canceller gave, if any.</summary>
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Who asked for the change, by name, for the notification emails. For a change the worker
    /// carried out later, the person who scheduled it.
    /// </summary>
    public string? ActorName { get; set; }

    /// <summary>
    /// Set on member events: the seat this is about. What the mail checks before sending, because
    /// the event is written before the seat and only the seat says whether it really changed.
    /// </summary>
    public string? AssignmentId { get; set; }

    /// <summary>Set on member events: who was given or lost the seat.</summary>
    public string? MemberUserId { get; set; }

    /// <summary>
    /// Set on member events, from IAM at the time of the change. Absent when IAM could not say,
    /// in which case the seat still changed and nobody is mailed.
    /// </summary>
    public string? MemberEmail { get; set; }

    public string? MemberDisplayName { get; set; }

    public string? MemberLanguage { get; set; }

    /// <summary>Set on member events: the organization's name as IAM gave it.</summary>
    public string? OrganizationName { get; set; }

    /// <summary>Set on credit events: the grant the event is about.</summary>
    public string? CreditGrantId { get; set; }

    /// <summary>Set on credit events: the seat the credits belong to, or null for the shared pool.</summary>
    public int? SeatNumber { get; set; }

    /// <summary>Set on credit events: how many credits the event added or moved.</summary>
    public decimal? CreditQuantity { get; set; }

    /// <summary>Set on credit events: what the grant holds afterwards.</summary>
    public decimal? CreditRemaining { get; set; }

    /// <summary>Set on a move: the seat the credits came from, or null for the shared pool.</summary>
    public int? FromSeatNumber { get; set; }

    /// <summary>
    /// <c>AutomaticCharge</c> or <c>OfflineInvoice</c> (spec 006), so a consumer can tell a
    /// bank-transfer contract from a card subscription without reading it back.
    /// </summary>
    public string? CollectionMethod { get; set; }

    /// <summary>When a scheduled change takes effect.</summary>
    public DateTime? EffectiveAtUtc { get; set; }

    /// <summary>
    /// Set on an extended discount or tax edit: the terms billed until <see cref="EffectiveAtUtc"/>.
    /// </summary>
    public PriceTerms? PreviousPriceTerms { get; set; }

    /// <summary>Set on an extended discount or tax edit: the terms billed from then on.</summary>
    public PriceTerms? PriceTerms { get; set; }

    /// <summary>Set on an organization credit purchase (spec 005): the purchase record.</summary>
    public string? CreditPurchaseId { get; set; }

    /// <summary>Set on an organization credit purchase: the meter's name as the invoice shows it.</summary>
    public string? MeterDisplayName { get; set; }

    /// <summary>Set on an organization credit purchase: what was charged, in minor units.</summary>
    public long? CreditTotalMinor { get; set; }

    public string? CurrencyCode { get; set; }

    /// <summary>Set on organization credit events: when the grant's credits lapse, or null for never.</summary>
    public DateTime? CreditExpiresAtUtc { get; set; }

    /// <summary>
    /// Set on an organization credit purchase: the account whose billing contact is mailed the
    /// confirmation. Cleared with the payload once the event is published.
    /// </summary>
    public string? BillingAccountId { get; set; }

    /// <summary>Set on an organization credit purchase that bought a pack.</summary>
    public string? PackCode { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>One quantity item as it stood before and after a change.</summary>
public sealed class LifecycleQuantityChange
{
    public string ItemKey { get; set; } = string.Empty;

    public string UnitLabel { get; set; } = string.Empty;

    public long PreviousQuantity { get; set; }

    public long Quantity { get; set; }
}
