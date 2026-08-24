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
| `SubscriptionWorkSchedulerBackgroundService` | The worker loop. |

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

## Rollout

`Subscription:SchedulerEnabled` is **off** by default.

- **Off** — the reconciliation sweep executes work exactly as it always has. Nothing changes.
- **On** — the sweep stops executing and starts *scheduling*: it becomes the repair path that
  discovers work the producers missed, and the scheduler runs it.

Never both. Executing in the sweep and scheduling the same work would run it twice, and twice is a
second charge.

The value is captured **once per process**, by `SubscriptionSchedulerMode`, and both the sweep and
the scheduler read that one copy. Asked separately, a configuration reload between two reads gives
one answer to one of them and the other answer to the other — and both mismatches are damaging in
opposite directions: flip it on while the scheduler has already decided it is idle and the sweep
schedules work nothing drains; flip it off mid-loop and the same renewal is charged twice. Changing
the mode therefore takes a restart, which is the honest cost of a switch that decides who moves
money.

Neither reading nor writing the collection is possible without its indexes: `ScheduleAsync` and
`ClaimDueAsync` both establish them first, and the guarantee lives in the queue rather than in a
caller's discipline. Skipping it would not merely risk a duplicate job — a duplicate written before
the unique index exists is one the index can never afterwards be built over, so the hole would hold
itself open.

### Two ways to change the mode

`Subscription:SchedulerCoordinationEnabled` decides which.

| | Coordination **off** (default) | Coordination **on** |
| --- | --- | --- |
| Who decides the mode | each process, from its own configuration | the fleet, from one record in the root database |
| `SchedulerEnabled` means | this process's mode | this process's **vote** |
| Changing it takes | a full fleet stop and start | a rolling deployment |
| Mixed modes during a change | possible, for as long as the roll takes | prevented |
| Root-database dependency | the queue only | the queue and the fleet record |

Off is the behaviour described above: the mode is read once per process, believed immediately, and a
rolling restart leaves direct-mode and queue-mode replicas side by side. What protects money inside
that window is the same thing that protects a retry — every handler's provider idempotency key comes
from persisted identity, a renewal from its period and attempt, a settlement from its reservation —
so two replicas running one tenant's renewal converge on one charge. That is wasted work and
duplicated log lines rather than duplicated money, but it is not a window to leave open on purpose.

#### Activation with coordination off

**A full fleet restart, not a rolling one.** Every worker logs its mode at warning on startup, so
which mode a replica is in is answerable from its logs rather than inferred.

1. Merge and deploy with `SchedulerEnabled` **off**. This is the default, and off means the sweep
   behaves exactly as it did before the queue existed — so a mixed-version fleet is still a fleet
   doing one thing.
2. Confirm every replica is on the new version.
3. Stop **all** worker replicas.
4. Set `SchedulerEnabled=true`.
5. Start the whole fleet.
6. Confirm from the logs that every replica reports the queue mode, that indexes were established,
   and that queue depth is draining rather than growing.

A rolling restart at steps 3–5 leaves direct-mode and queue-mode replicas running side by side for as
long as the roll takes. That is the window coordination closes.

### Fleet coordination

With coordination on, the fleet holds **one** record of the mode in force, and a replica runs what
that record says rather than what its own configuration says.

`BlocksRootDb.SubscriptionSchedulerMode` — one document: the desired mode, and a **generation** that
advances once per change. The generation, not the mode, is what replicas coordinate on: a mode alone
cannot tell "we have always been in Direct" from "we went to Queue and came back", and a replica that
missed the round trip would believe it was in step.

`BlocksRootDb.SubscriptionSchedulerReplicas` — one document per worker: what it is configured for,
what it is running, the generation it has reached, whether it is running, draining or drained, and a
heartbeat.

Every pass, each replica publishes its own row and reads the others'. Three rules follow, and the
whole guarantee is in the second:

1. **No record yet** — write one from this replica's own configuration. Losing that race is not a
   failure: the winner's record says what this one would have.
2. **The record names a generation this replica has not reached** — stop taking new work, wait for
   whatever it already holds to finish, then report the new generation. Start in the mode the record
   names only once **no other live replica is behind that generation**. That is the barrier: a rolled
   pod cannot begin draining the queue while a pod nobody has restarted yet is still executing the
   same work directly.
3. **Settled, and every live replica's configuration disagrees with the record in the same
   direction** — propose the change, conditional on the generation just read. Unanimity is the
   anti-flap rule: a change takes effect once its deployment has finished rolling, and one pod left
   on stale configuration can never drag the fleet back. Two replicas proposing at once produce one
   generation rather than two.

A **draining** replica still blocks. It keeps reporting the generation it is actually still in until
it holds nothing, because a replica that claimed the new generation while a provider call was open
would be telling the fleet it had finished when it had not.

So a mode change is: deploy the configuration to every replica, and the fleet takes it up by itself a
few poll intervals after the last pod comes up. Every step is a warning-level log line — proposed,
draining, waiting for whom, now running in which mode at which generation.

#### What holds it together, and what it costs

**Timestamps come from the database.** Heartbeats are written with `$currentDate` and liveness is
evaluated with `$$NOW`, so both sides of the comparison come from one clock. Replicas compare each
other's heartbeats, and comparing timestamps written by different machines is how a replica that is
still working comes to look expired.

**An unreachable root database does not stop work.** A replica that cannot read the record keeps
running in the mode it is already in. No change can be in flight that it does not know about, because
a change cannot complete without its own acknowledgement — so carrying on is both safe and the only
option that keeps money moving through a database blip.

**A replica stops itself before the fleet stops waiting for it.** `SchedulerReplicaExpirySeconds`
(default 900) is how long a silent replica is still waited for; a replica that has not managed to
write its own row for that window less a margin closes its gate and does nothing. That ordering is
the whole reason the expiry can be trusted: by the time the others ignore a replica, it has already
stopped. The cost is real — a root database unreachable for a quarter of an hour pauses background
work rather than risking two modes at once, which is the same trade the rest of this directory makes.

**A pod that stops politely removes its own row**, so a planned restart is an immediate handover
rather than a fifteen-minute wait.

**The mode is not authored over HTTP.** It is a platform-wide switch and this service has no
platform-administrator role — only `[Authorize]`, which every tenant's users satisfy — so an endpoint
for it would let any authenticated caller stop or start every tenant's billing. Configuration is the
authoring surface, and the fleet record is only how replicas agree on what it says.

#### Activation with coordination on

1. Deploy with `SchedulerCoordinationEnabled=true` and `SchedulerEnabled` unchanged. Nothing changes
   mode: the fleet seeds its record from the mode already running.
2. Confirm from the logs that every replica reports the same generation and mode.
3. Roll out `SchedulerEnabled=true`. During the roll, replicas configured for Queue keep running
   Direct, and say so at warning.
4. When the last pod is up, one replica proposes, every replica drains, and the fleet reports
   `mode now Queue at generation N`.
5. Confirm queue depth is draining rather than growing.

Reverting is the same in the other direction, and needs no stop.

#### Not solved

**A replica that is hung rather than dead.** A process that stops heartbeating while still inside a
provider call becomes invisible to the barrier once its row expires. The self-fence closes this for a
process that is still *running* — it checks the deadline every pass — but not for one wedged inside a
single call for longer than the expiry window. The queue's lease and the provider's idempotency key
are what remain between that and a double charge.

**Payment.** `Payment.DomainService/Scheduling` has the same mode switch and none of this. It matters
less there, because the mode it replaces is *nothing running* rather than a second executor, so a
mixed fleet under-recovers rather than double-executes. These mechanics should move with the rest when
the two schedulers are merged — see that module's README for which direction that goes.

Once the queue is trusted, the sweep's interval can be lengthened
(`Subscription:ReconciliationPollSeconds`) so it becomes a genuine repair pass rather than a second
scheduler. Removing it is a separate decision and needs its own review.

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
| `SchedulerEnabled` | `false` | Whether the queue drives work. |
| `SchedulerPollSeconds` | `10` | Idle poll interval. A busy worker goes straight to the next batch. |
| `SchedulerBatchSize` | `20` | Items claimed per pass. |
| `SchedulerMaxParallelism` | `4` | Items run at once. Bounded: this work talks to a payment provider. |
| `SchedulerLeaseSeconds` | `120` | How long a claim holds an item. |
| `SchedulerMaxAttempts` | `5` | Attempts before dead-lettering. |
| `SchedulerRetryBaseSeconds` | `30` | Backoff base, doubled per attempt, jittered. |
| `SchedulerRetryMaxSeconds` | `3600` | Backoff cap. |
| `SchedulerCompletedRetentionDays` | `14` | How long completed records are kept. |
| `SchedulerSweepBucketMinutes` | `5` | The occurrence bucket the repair sweep schedules into. |
| `SchedulerCoordinationEnabled` | `false` | Whether the fleet agrees the mode through the root database. |
| `SchedulerCoordinationPollSeconds` | `5` | How often a replica publishes its state and reads the fleet's. Also a handover's step size. |
| `SchedulerReplicaExpirySeconds` | `900` | How long a silent replica is waited for. A replica fences itself a margin inside this. |

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

Counters and histograms are tagged with the work type, and failures with the **error code** — a
bounded set. Provider messages are never tags: unbounded values are a cardinality explosion in
whatever collects them.

The two gauges report what the last idle pass measured rather than querying on collection. Depth is
an aggregation over a collection in another database, and a collector should not get to decide when
that query runs.

Alert rules belong in the monitoring stack rather than here, but two are worth stating: any
`dead_lettered` above zero, and `oldest_due_age` beyond a per-work-type threshold — tighter for
settlement recovery and renewal than for the outbox.

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
