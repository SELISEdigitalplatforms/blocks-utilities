# Subscription background work scheduler

A durable queue in the platform's root database (`BlocksRootDb`), so a worker can find work that is
due without walking a roster of every tenant.

## Why

`SubscriptionReconciliationBackgroundService` refreshed the whole tenant roster and processed tenants
in order, running six checks against each one. With ~2,400 tenants, a renewal due for a tenant late
in the roster waited behind thousands of tenants that had nothing to do, and the wait was
proportional to tenants that *exist* rather than to work outstanding.

Parallelising the roster scan would not have fixed it. The scan itself is the cost, and the work
moves money — so the answer is to know what is due before going to look.

## Shape

| Piece | What it does |
| --- | --- |
| `SubscriptionBackgroundWork` | One scheduled occurrence. Root database. No card data, no secrets, no provider payloads. |
| `AggregateId` on an item | Names the subscription the work is about. Set by producers at the point of change; empty on items the repair sweep schedules, because its job is to find what nobody named. |
| `ISubscriptionWorkQueue` | Schedule, claim, renew, complete, fail, list dead letters, report depth. |
| `ISubscriptionWorkScheduler` | The producer seam. Idempotent by occurrence, and assigns priority. |
| `ISubscriptionWorkHandler` | Runs one kind of work. Thin: it delegates to the processor that already owns the rules. |
| `SubscriptionWorkDispatcher` | Claims a bounded batch, runs it with bounded parallelism, records the outcome. |
| `SubscriptionWorkSchedulerBackgroundService` | The worker loop. Starts unconditionally; the only executor. |
| `SubscriptionRepairAnnouncer` | Finds work a tenant owes that nothing announced, and announces it. Holds no processor. |
| `SubscriptionQueueMandate` | States once per process that the queue is mandatory, and warns about retired settings. |
| `SubscriptionQueueReadiness` | What the drainer has actually managed, for the health check and the gauges. |
| `SubscriptionQueueHealthCheck` | `GET /health/ready`. Root database, required indexes, claim query, drainer state. |

## The two databases

`BlocksRootDb` is the scheduling layer. Subscriptions, payments and usage stay authoritative in their
tenant databases, and **there is no transaction across the two**. Three things therefore have to be
survivable, and are:

- **Tenant state committed, scheduling write lost.** The repair sweep finds it.
- **Scheduling write committed, tenant state moved on.** The handler re-reads tenant state and
  decides there is nothing to do.
- **Worker died after the provider succeeded, before completion.** The lease expires, the item is
  reclaimed, and the handler's own provider idempotency key — derived from persisted identity, not
  from the attempt — finds the charge that already exists instead of raising another.

That last point is why handlers stay thin. A renewal keys its charge on the period and attempt
number; a settlement keys it on the reservation id. Reimplementing either here would give the same
money two sets of rules.

## The queue is the only executor

Every renewal, activation settlement, activation recovery, settlement-reservation recovery, usage
period closure, usage invoice charge, outbox publication, financial-document issue and
financial-document delivery runs from a **claimed queue item**. There is no second path and no
setting that selects one.

`SubscriptionWorkSchedulerBackgroundService` starts unconditionally. A setting able to stop that loop
would be a setting that stops billing, so there is not one.

### The sweep may only announce

`SubscriptionReconciliationBackgroundService` walks the tenant roster, asks each tenant what it owes,
and enqueues one idempotent occurrence per work type. It then stops. The announcing itself lives in
`SubscriptionRepairAnnouncer`, which is constructed with **no processor at all** — so "the sweep
cannot charge anybody" is a property of what it can reach rather than a claim about what it happens to
call.

This is the change worth understanding. The previous design chose, from configuration, whether the
sweep executed the work or the queue did:

- Wrong one way — the sweep executing while the queue drained — and one renewal is charged twice.
- Wrong the other way — neither running — and nobody is billed at all.

Both readings came from a setting each service read separately, so a configuration reload between two
reads could give them different answers. Announcing is safe to repeat where executing is not: the
unique occurrence index collapses the sweep's announcement, the producer's at the point of change, and
another replica's sweep onto one item, so a duplicated announcement is a write that changes nothing.

The sweep is deliberately slower than the queue poll. It costs a query per tenant and exists for a
case that is rare by construction: a tenant write that committed while the scheduling write to the
root database did not.

### No fallback when the root database is unavailable

The drainer retries with capped backoff and keeps the process alive. It does **not** fall back to
executing work anywhere else, because a fallback is a second executor and a second executor is the
double charge above.

The cost of that choice is an outage in which nobody is billed, so the outage has to be loud rather
than quiet:

- `SubscriptionQueueReadiness` holds what the drainer has actually managed — indexes created, when a
  claim last succeeded, how long the current run of failures has lasted.
- `SubscriptionQueueHealthCheck` is registered in the Api as `subscription-work-queue`, tagged `ready`
  and served at **`GET /health/ready`**. It probes the root database, the required indexes and the
  claim query, and reads the drainer's own state.
- A brief run of failures reports **Degraded**; past two minutes it reports **Unhealthy**. A failover
  drops a pass or two, and paging on the first one teaches people to ignore the page.

`ProbeAsync` runs the claim's query as a *read*. A probe that claimed would lease work to a process
that is not going to run it, delaying a renewal by one lease per probe.

Only three indexes are required before draining: the due index, the expired-lease index, and the
unique occurrence index. Diagnostics and the TTL are absent from that list on purpose — without them
the collection is slower to investigate and slower to purge, which is not the same as unsafe. Without
the occurrence index two producers can create two items for one billing period, so a missing one is a
refusal to drain rather than a slow query.

### Retired configuration

`Subscription:SchedulerEnabled` and `Subscription:SchedulerCoordinationEnabled` are **ignored**. They
remain bindable for one compatibility release so a rollout carrying them does not fail on an unknown
key, and `SubscriptionQueueMandate` warns at startup naming what it read and what it is doing instead.
A setting that is silently ignored is worse than one that is rejected: the operator goes on believing
it did something.

Both are `bool?` so an absent setting and an explicit `false` can be told apart in that warning. They
mean different things to whoever reads it — one is a deployment already cleaned up, the other an
operator who believes they have turned the execution path off.

`SchedulerCoordinationEnabled` coordinated a fleet through a changeover between two modes. With one
mode there is nothing to coordinate: every replica drains the same queue, and the occurrence index and
the claim lease already keep them from colliding.

### Deploying it

The mode switch, the fleet handover and the replica records are gone from the code. This version is
safe to deploy only once **no replica is still running in the old Direct mode** — a Direct replica
executes work in its sweep, and these replicas drain the same work from the queue, which is the double
charge this design exists to remove.

1. On the previous version, move the whole fleet to Queue mode and confirm every replica reports it.
2. Watch the backlog drain, and confirm `FinancialDocumentIssue` is writing to each tenant's
   `SubscriptionFinancialDocuments` rather than to `BlocksRootDb`.
3. Deploy this version. Existing pending items are claimed and processed as they are; nothing needs a
   tenant-database edit.
4. Leave the retired settings bindable for one release, then delete them along with the obsolete root
   mode and replica collections.

Neither reading nor writing the collection is possible without its indexes: `ScheduleAsync` and
`ClaimDueAsync` both establish them first, and the guarantee lives in the queue rather than in a
caller's discipline. Skipping it would not merely risk a duplicate job — a duplicate written before
the unique index exists is one the index can never afterwards be built over, so the hole would hold
itself open.

## Producers

Work is announced where the state changes, so a tenant with nothing due generates nothing:

| Event | Work | Occurrence key | Due |
| --- | --- | --- | --- |
| A renewal succeeds | `Renewal` | the new period | when that period ends |
| A settlement reservation is taken | `SettlementReservationRecovery` | the reservation | after the reservation grace window |
| A subscription is created unpaid | `ActivationRecovery` | the subscription | after the initial-charge grace window |
| A usage window closes and rates | `UsageInvoiceCharge` | the usage period | now — the invoice exists |

Every key is derived, so a producer that runs twice, or a sweep that finds the same thing, lands on
one occurrence. Every producer is best effort: by the time one runs, what it announces has already
happened, and a scheduling write in another database that fails must not undo or fail it. The sweep
is what covers the miss.

The **when** lives in the scheduler rather than at each call site, because the grace windows are read
by the sweep too and several services announce the same kinds of work. Duplicated, they drift — and a
due instant that drifts is work that runs at the wrong time.

## Settings

All under `Subscription:`.

| Setting | Default | Meaning |
| --- | --- | --- |
| `SchedulerEnabled` | *unset* | **Ignored.** Bindable for one release; warned about at startup. |
| `SchedulerPollSeconds` | `10` | Idle poll interval. A busy worker goes straight to the next batch. |
| `SchedulerBatchSize` | `20` | Items claimed per pass. |
| `SchedulerMaxParallelism` | `4` | Items run at once. Bounded: this work talks to a payment provider. |
| `SchedulerLeaseSeconds` | `120` | How long a claim holds an item. |
| `SchedulerMaxAttempts` | `5` | Attempts before dead-lettering. |
| `SchedulerRetryBaseSeconds` | `30` | Backoff base, doubled per attempt, jittered. |
| `SchedulerRetryMaxSeconds` | `3600` | Backoff cap. |
| `SchedulerCompletedRetentionDays` | `14` | How long completed records are kept. |
| `SchedulerSweepBucketMinutes` | `5` | The occurrence bucket the repair sweep schedules into. |
| `SchedulerCoordinationEnabled` | *unset* | **Ignored.** There is one execution mode, so there is nothing to coordinate. |
| `SchedulerUnclaimedAlertSeconds` | `900` | How long due work may sit unclaimed before the drainer warns. Floored at 60s. |

## Retention

The TTL index is on `PurgeAtUtc`, and `PurgeAtUtc` is set **only on completion**. Pending, processing
and dead-lettered records have no value there, and a TTL index ignores documents whose field is
absent — so unfinished work and work somebody has to look at are never removed automatically.

## Operating it

A held lease is renewed at half the lease for as long as its handler runs, and a renewal that keeps
*failing* is treated as a lost lease once the last confirmed lease runs out — a failed call is not
proof the claim is gone, but time passing is. A held lease is renewed at half the lease for as long
as its handler runs, so work that outlives its
claim is not reclaimed and run a second time while the first attempt is still inside a provider call.
A renewal that comes back false means the item is already somebody else's: the handler's token is
cancelled, and the attempt records **neither** completion nor failure, because the current holder
decides that item. A completion the queue refuses is logged and not counted as processed — reporting
it as success is how an item that ran twice looks like one that ran once.

Every transition logs the item id, work type, occurrence key, tenant, subscription and organization
ids, correlation and operation ids, lease id, attempt count, and duration. Identifiers are written in
clear rather than hashed — they name records, not people, and `PaymentLogValue` says as much: an
operator holding a subscription id has to be able to find its lines without recomputing a digest.
Personal data still goes through `Hash`. See [TRACE.md](TRACE.md) for the whole chain from an API call
to a provider charge, including what it does *not* guarantee. An idle pass logs queue depth
and the oldest due age per work type, which is the shape that shows a queue that is not draining.

Dead-lettered work logs at **error** — it is the one outcome nothing else will pick up — is listable
through `ListDeadLetteredAsync`, and emits an **audit event** (`Stage: DeadLettered`, `Outcome:
Abandoned`) carrying the correlation, the attempt and the error classification. Logs rotate and are
addressed to whoever is on call; the audit event is addressed to whoever asks months later why a
subscription stopped billing. A transient retry is not audited — it is a delay, not a decision, and
auditing every one would bury the abandonment that matters.

## Operator recovery

`/api/subscription-background-work/dead-letters` lists what has been given up on, and
`.../{id}/requeue` and `.../{id}/abandon` are the two things that can be done about it. Both scope to
the caller's own tenant: the work item id is a platform-wide identifier, so without that check any
authenticated caller could act on another tenant's work by naming it. Another tenant's item answers
*not found* rather than *forbidden*, because "forbidden" confirms that an item with that id exists
and whose it is.

**Requeue is one write.** Status, attempt count and lease are cleared together. Any two of the three
leaves the item reachable but stuck — a stale lease makes it unclaimable, attempts at the ceiling
dead-letter it again on its first failure — and both look exactly like a requeue that did nothing.
That is what made a hand-edited database the wrong tool for this.

Requeueing does **not** decide the work is still due. The handler re-reads tenant state and decides
that, which is the difference between an operator saying *try again* and an operator saying *charge
this*. A month-old renewal requeued today finds its subscription has moved on and completes without
billing anybody — which is why the listing states each item's age rather than leaving it to be
worked out from two timestamps.

**Abandoned is its own status**, not a second flavour of dead-lettered. Dead-lettered means the
system stopped trying; abandoned means somebody looked and decided it must not be tried. Collapsing
them would leave an operator unable to tell what still needs a decision from what has already had
one. Neither is ever purged.

A reason is required for both, and recorded with the actor in an audit event. A dead letter set aside
without one is a decision nobody can review, and reviewing them is the only reason to keep them.

## Metrics

Published on a `Meter` named `Blocks.Subscription.BackgroundWork`, using the framework's own
instruments and the worker exports this meter through OTLP. The collector endpoint follows the
standard `OTEL_EXPORTER_OTLP_ENDPOINT` configuration. Prometheus alert rules and a Grafana dashboard
are versioned under `monitoring/`.

| Instrument | Kind | What it answers |
| --- | --- | --- |
| `subscription.work.claimed` | counter | are workers picking work up |
| `subscription.work.completed` | counter | is it finishing |
| `subscription.work.retried` | counter | is a dependency degrading |
| `subscription.work.dead_lettered` | counter | **alert on any value above zero** |
| `subscription.work.lease_lost` | counter | are leases shorter than the work they cover |
| `subscription.work.duration` | histogram | which handler is slow, before it becomes a lease problem |
| `subscription.work.lag` | histogram | how late work is picked up — the number that means "a renewal is late" |
| `subscription.work.queue_depth` | gauge | is the queue filling faster than it drains |
| `subscription.work.oldest_due_age` | gauge | a queue can be shallow and still fail to drain the one thing that matters |
| `subscription.work.repair_announced` | counter | is the sweep covering for producers that are losing their scheduling writes |

Counters and histograms are tagged with the work type, and failures with the **error code** — a
bounded set. Provider messages are never tags: unbounded values are a cardinality explosion in
whatever collects them.

The two gauges report what the last idle pass measured rather than querying on collection. Depth is
an aggregation over a collection in another database, and a collector should not get to decide when
that query runs.

Alert rules belong in the monitoring stack rather than here, but four are worth stating: any
`dead_lettered` above zero; `oldest_due_age` beyond a per-work-type threshold — tighter for
settlement recovery and renewal than for the outbox, and tighter again for `FinancialDocumentIssue`
and `FinancialDocumentDelivery`, where the age *is* the invoice being late; `/health/ready` unhealthy
on any replica, which now means nothing is being billed rather than one path being down; and
`repair_announced` steadily above zero, which says producers at the point of change are losing their
scheduling writes and the sweep is quietly covering for them. The queue draining normally is not
evidence that those producers work.

## Not in this slice

- Payment-module work types (reconciliation, webhook recovery, provider refresh, cleanup). The
  ticket lists them; they belong to `PaymentBackgroundWork` and a separate producer set.
- An outbox producer, deliberately. It is the lowest-priority work in the queue — an event that
  publishes a sweep interval late is a notification that arrives late — and the only place to
  produce from is the repository write that appends the event, which is the one layer that must not
  reach into the root database. The sweep is the right producer for it.
- A usage-closure producer at the point the *previous* window closes. Closure is announced when a
  subscription is created or renewed; a window that closes and immediately opens another relies on
  the sweep for the next one.
- Cross-tenant operator views. Recovery answers for the caller's own tenant only; a platform-wide
  view of every tenant's dead letters is a different question, with a different answer about who may
  ask it.
