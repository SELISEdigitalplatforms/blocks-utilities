namespace Subscription.DomainService.Utilities;

public sealed class SubscriptionOptions
{
    public const string SectionName = "Subscription";

    /// <summary>
    /// How long a resolved subscription may be served from memory before it is read again.
    /// Entitlement tolerates this staleness deliberately; usage counters never do and are
    /// never cached.
    /// </summary>
    public int EntitlementCacheSeconds { get; set; } = 10;

    /// <summary>
    /// How long a finished usage period's counter is kept after it ends. The counter is a
    /// derived read model and can always be rebuilt from the ledger, which is never expired.
    /// </summary>
    public int CounterRetentionDays { get; set; } = 400;

    /// <summary>
    /// How long a subscription may sit unpaid before the initial charge is considered
    /// abandoned. Covers the shopper who opens checkout and closes the tab.
    /// </summary>
    public int InitialChargeGraceMinutes { get; set; } = 60;

    /// <summary>
    /// How often the worker sweeps for subscriptions whose activation never completed. Without
    /// a tick, a subscription whose activation lost a compare-and-set stays unpaid-looking
    /// forever while the shopper's money has already moved.
    /// </summary>
    public int ReconciliationPollSeconds { get; set; } = 120;

    public int ActivationBatchSize { get; set; } = 50;
    public int ActivationMaxAttempts { get; set; } = 10;
    public int ActivationRetrySeconds { get; set; } = 30;

    public int RenewalBatchSize { get; set; } = 50;

    /// <summary>
    /// How long a quantity increase may hold its reservation before the sweep decides its caller
    /// is never coming back. Long enough to cover a slow authorization, short enough that a
    /// subscriber is not left unable to change quantity again.
    /// </summary>
    public int SettlementReservationGraceMinutes { get; set; } = 15;

    public int SettlementReservationBatchSize { get; set; } = 50;

    /// <summary>
    /// Renewal attempts, including the first decline, before a subscription moves to
    /// <c>Unpaid</c>. Retrying beyond this is treated as certain to fail again rather than
    /// eventually succeeding.
    /// </summary>
    public int DunningMaxAttempts { get; set; } = 4;

    /// <summary>
    /// A fixed interval between dunning attempts, not exponential backoff: this is a business
    /// cadence for asking a customer to fix a card, not load-shedding against a failing
    /// dependency.
    /// </summary>
    public int DunningRetryIntervalHours { get; set; } = 24;

    public int UsageRatingBatchSize { get; set; } = 50;

    /// <summary>
    /// Overage-charge attempts, including the first decline, before an invoice is abandoned.
    /// Independent of <see cref="DunningMaxAttempts"/>: a failed overage charge never affects
    /// the subscription itself, so it is free to have its own, more relaxed cadence.
    /// </summary>
    public int UsageRatingMaxAttempts { get; set; } = 3;

    public int UsageRatingRetryHours { get; set; } = 24;

    public int OutboxBatchSize { get; set; } = 50;
    public int OutboxLeaseSeconds { get; set; } = 30;
    public int OutboxMaxAttempts { get; set; } = 10;

    public int UsageRequestsPerMinute { get; set; } = 600;
    public int EntitlementRequestsPerMinute { get; set; } = 1_200;

    /// <summary>
    /// Caps on the free-form metadata a usage record may carry. Billing needs a count, not a
    /// dossier: without a bound this field becomes an unversioned side-channel for whatever
    /// the calling product happens to hold, including personal data it should not put here.
    /// </summary>
    public int MaximumUsageMetadataEntries { get; set; } = 10;
    public int MaximumUsageMetadataValueLength { get; set; } = 256;

    /// <summary>
    /// Ignored. Kept bindable for one compatibility release.
    /// </summary>
    /// <remarks>
    /// The durable queue is the only path subscription background work has, so there is nothing for
    /// this to switch: <c>false</c> would have to mean "do not bill anybody". It is still accepted so
    /// a rollout carrying the old configuration does not fail on an unknown key, and
    /// <see cref="Scheduling.SubscriptionQueueMandate"/> warns at startup when it is present.
    /// <para>
    /// Nullable so an absent setting and an explicit <c>false</c> can be told apart in that warning.
    /// They mean different things to whoever reads it: one is a deployment already cleaned up, the
    /// other an operator who believes they have turned the queue off.
    /// </para>
    /// </remarks>
    [Obsolete("Subscription queue execution is mandatory. Remove this setting; it is ignored.")]
    public bool? SchedulerEnabled { get; set; }

    /// <summary>
    /// Ignored. Kept bindable for one compatibility release.
    /// </summary>
    /// <remarks>
    /// This coordinated a fleet through a changeover between two execution modes. With one mode there
    /// is nothing to coordinate: every replica drains the same queue, and the occurrence index and the
    /// claim lease already keep them from colliding.
    /// </remarks>
    [Obsolete("There is only one execution mode, so there is nothing to coordinate. Ignored.")]
    public bool? SchedulerCoordinationEnabled { get; set; }

    /// <summary>
    /// How long due work may sit unclaimed before the drainer says so at warning.
    /// </summary>
    /// <remarks>
    /// The queue being deep is normal under load; the oldest pending item being old is not, and it is
    /// the shape that means a tenant's renewal or invoice is late. Floored at a minute, because a
    /// threshold shorter than a poll interval plus a batch would fire on ordinary throughput.
    /// </remarks>
    public int SchedulerUnclaimedAlertSeconds { get; set; } = 900;

    /// <summary>
    /// The same, for financial-document issue and delivery. Deliberately tighter.
    /// </summary>
    /// <remarks>
    /// Those two are the lowest-priority work in the queue, so they are the first to be starved by a
    /// sustained backlog of renewals and recovery. They are also the two where the age <em>is</em>
    /// what a subscriber sees: a payment taken with no invoice issued, or an invoice issued and never
    /// delivered. Ordinary repair work running late costs a delay nobody outside notices.
    /// </remarks>
    public int SchedulerDocumentUnclaimedAlertSeconds { get; set; } = 300;

    /// <summary>
    /// How often the drainer measures queue depth, whatever the last batch did.
    /// </summary>
    /// <remarks>
    /// Interval-driven rather than idle-driven, and that is a fix rather than a preference: depth
    /// used to be reported only after an empty batch, so a queue with something to claim on every
    /// pass never reported its own backlog. The shape that hid was the one worth alerting on.
    /// <para>
    /// Not every pass, because it is an aggregation over another database and a busy drainer's own
    /// throughput lines already say the queue is moving.
    /// </para>
    /// </remarks>
    public int SchedulerDepthReportSeconds { get; set; } = 30;

    /// <summary>How often a drainer publishes its own liveness to the root database.</summary>
    public int SchedulerWorkerHeartbeatSeconds { get; set; } = 15;

    /// <summary>
    /// How long since a drainer's last heartbeat before readiness treats it as gone.
    /// </summary>
    /// <remarks>
    /// Several heartbeats wide, so one missed write during a failover does not empty the fleet. This
    /// is the window in which "nothing is draining" becomes reportable, and reporting it early is a
    /// false alarm while reporting it late is billing quietly stopped.
    /// </remarks>
    public int SchedulerWorkerLivenessSeconds { get; set; } = 90;

    /// <summary>
    /// How recently a live drainer must have claimed for readiness to call it draining.
    /// </summary>
    /// <remarks>
    /// Separate from liveness because the two failures point somewhere different: a replica that is
    /// alive and cannot claim is a database problem, and a replica that has stopped reporting is a
    /// deployment problem. Wide enough to cover a poll interval plus a slow batch.
    /// </remarks>
    public int SchedulerWorkerClaimWindowSeconds { get; set; } = 180;

    /// <summary>
    /// How long a stopped drainer's registry record is kept before the TTL removes it.
    /// </summary>
    /// <remarks>
    /// A TTL rather than a delete on shutdown, because a killed pod never gets to tidy up and a
    /// registry that only removed records politely would fill with the ones that crashed.
    /// </remarks>
    public int SchedulerWorkerRetentionSeconds { get; set; } = 3_600;

    /// <summary>
    /// How long a silent replica is still waited for before the fleet moves without it.
    /// </summary>
    /// <remarks>
    /// Deliberately long. A replica that has gone quiet may still be working, so this is the window
    /// in which a mode change waits rather than risking two modes at once — and fifteen minutes of
    /// waiting for a pod that is genuinely gone costs a delayed switch, while not waiting costs the
    /// guarantee the switch exists for. A replica stops itself a margin inside this window, so by
    /// the time the fleet stops waiting it has already stopped working.
    /// </remarks>
    public int SchedulerReplicaExpirySeconds { get; set; } = 900;

    /// <summary>
    /// How often a worker asks the queue for due work.
    /// </summary>
    /// <remarks>
    /// Short on purpose, and affordable: unlike the sweep, an empty poll is one indexed query
    /// against one collection rather than a walk through every tenant's database.
    /// </remarks>
    public int SchedulerPollSeconds { get; set; } = 10;

    /// <summary>How many items one worker claims per pass.</summary>
    public int SchedulerBatchSize { get; set; } = 20;

    /// <summary>
    /// How many claimed items one worker runs at once. Bounded because this work talks to a
    /// payment provider, and unbounded fan-out trades a latency problem for a rate-limit one.
    /// </summary>
    public int SchedulerMaxParallelism { get; set; } = 4;

    /// <summary>
    /// How long a claim holds an item before another worker may take it.
    /// </summary>
    /// <remarks>
    /// Long enough to outlast a slow provider call, short enough that a crashed worker's items come
    /// back in the same shift. Work that can exceed it renews the lease rather than raising it for
    /// everything.
    /// </remarks>
    public int SchedulerLeaseSeconds { get; set; } = 120;

    public int SchedulerMaxAttempts { get; set; } = 5;

    public int SchedulerRetryBaseSeconds { get; set; } = 30;

    public int SchedulerRetryMaxSeconds { get; set; } = 3_600;

    /// <summary>
    /// How long a completed record is kept before the TTL index removes it. Pending, processing and
    /// dead-lettered records are never purged: they carry unfinished money.
    /// </summary>
    public int SchedulerCompletedRetentionDays { get; set; } = 14;

    /// <summary>
    /// How long a tenant's scheduled sweep occurrence covers, in minutes.
    /// </summary>
    /// <remarks>
    /// The repair sweep schedules one occurrence per tenant per work type per bucket of this
    /// length, so a sweep that overlaps itself — or two workers sweeping at once — produces one item
    /// rather than two.
    /// </remarks>
    public int SchedulerSweepBucketMinutes { get; set; } = 5;

    /// <summary>
    /// Pins background sweeps to specific tenants. Empty — the normal case — discovers them from
    /// the platform's tenant registry instead.
    /// </summary>
    /// <remarks>
    /// Kept as an override rather than the source of truth. A hand-maintained list is stale the
    /// moment the next project is created, and a tenant the sweep never visits is a tenant whose
    /// renewals silently never happen. Useful for pinning one tenant locally, and as an escape
    /// hatch if discovery ever misbehaves somewhere billing cannot wait.
    /// </remarks>
    public string[] TenantIds { get; set; } = [];

    /// <summary>
    /// How long a discovered tenant roster is reused before it is read again.
    /// </summary>
    /// <remarks>
    /// Generous on purpose. Nothing time-critical waits on this: a subscription activates from
    /// the payment webhook, which carries its own tenant and never consults the roster. The
    /// sweep only matters at the first renewal, a whole billing period later.
    /// </remarks>
    public int TenantRefreshSeconds { get; set; } = 300;

    /// <summary>
    /// Whether a paid subscription or money-moving change requires a complete billing profile.
    /// </summary>
    /// <remarks>
    /// On by default, because an invoice with a blank recipient is not a document anybody can use and
    /// the only moment it can be prevented is before the money moves. The switch exists for an
    /// installation mid-migration, where subscribers predate the profile and refusing their renewals
    /// would be worse than issuing an invoice addressed to their organization id — and it never
    /// affects renewals either way, only the changes a person initiates.
    /// </remarks>
    public bool RequireBillingProfile { get; set; } = true;

    /// <summary>
    /// How many times a document's PDF and email are attempted before it is abandoned.
    /// </summary>
    /// <remarks>
    /// Independent of every other retry budget here. A failed render never affects the subscription
    /// or the payment — the money is settled and the invoice is issued and numbered — so it is free
    /// to have its own cadence, and a generous one.
    /// </remarks>
    public int DocumentDeliveryMaxAttempts { get; set; } = 8;

    public int DocumentDeliveryBatchSize { get; set; } = 25;

    /// <summary>
    /// How far back a tenant's <em>first</em> document-recovery pass reaches.
    /// </summary>
    /// <remarks>
    /// Used once per tenant and never again. Every pass after it starts from the high-water mark the
    /// previous one stored, and those only move forward — so this is not an ongoing window and
    /// nothing can fall outside it later. It exists to decide how much pre-existing history a tenant
    /// picks up the first time the sweep runs against it.
    /// <para>
    /// Generous by default, because the cost is one indexed scan once, and the alternative is a
    /// tenant's older charges never being noticed at all.
    /// </para>
    /// </remarks>
    public int DocumentFirstPassReachDays { get; set; } = 400;

    /// <summary>How often the worker looks for documents whose PDF or email never completed.</summary>
    public int DocumentDeliveryPollSeconds { get; set; } = 120;

    /// <summary>
    /// The merchant identity printed on every document this installation issues.
    /// </summary>
    /// <remarks>
    /// Configuration rather than a stored entity, because it describes the installation itself: one
    /// tenant, one legal seller, one set of payment instructions. It is read once per document and
    /// then <em>copied onto it</em>, so changing this affects documents issued from that point on and
    /// nothing already sent.
    /// </remarks>
    public SubscriptionInvoicingOptions Invoicing { get; set; } = new();
}

/// <summary>Who the invoices say they are from.</summary>
public sealed class SubscriptionInvoicingOptions
{
    /// <summary>
    /// The seller's legal name. Empty is allowed and prints nothing rather than failing: a document
    /// with the customer, the period and the amounts on it is still worth issuing, and refusing to
    /// issue one over unset configuration would turn a letterhead problem into unbilled revenue.
    /// </summary>
    public string LegalName { get; set; } = string.Empty;

    public string? AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public string? City { get; set; }

    public string? Region { get; set; }

    public string? PostalCode { get; set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? CountryCode { get; set; }

    /// <summary>The seller's own VAT or tax registration number.</summary>
    public string? TaxRegistrationId { get; set; }

    /// <summary>Where a subscriber replies about a charge.</summary>
    public string? SupportEmail { get; set; }

    /// <summary>Bank details, terms, a VAT note. Rendered verbatim in the document footer.</summary>
    public string? PaymentInstructions { get; set; }
}
