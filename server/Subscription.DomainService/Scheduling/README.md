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

### Rolling deployments

**The mode is consistent within a process, not across a fleet.** Two replicas on different versions,
or with different configuration, can run in different modes at the same time — and nothing in this
process can detect that.

### Activation runbook

**Enabling the scheduler requires a full fleet restart, not a rolling one.** Every worker logs its
mode at warning on startup — `mode: DIRECT` or `mode: QUEUE` — so which mode a replica is in is
answerable from its logs rather than inferred.

1. Merge and deploy with `SchedulerEnabled` **off**. This is the default, and off means the sweep
   behaves exactly as it did before the queue existed — so a mixed-version fleet is still a fleet
   doing one thing.
2. Confirm every replica is on the new version.
3. Stop **all** worker replicas.
4. Set `SchedulerEnabled=true`.
5. Start the whole fleet.
6. Confirm from the logs that every replica reports `mode: QUEUE`, that indexes were established,
   and that queue depth is draining rather than growing.

A rolling restart at step 3–5 leaves direct-mode and queue-mode replicas running side by side for as
long as the roll takes.

What protects money inside that window is the same thing that protects a retry: every handler's
provider idempotency key comes from persisted identity — a renewal from its period and attempt, a
settlement from its reservation. A direct-mode replica and a queue-mode replica running the same
tenant's renewal therefore converge on one charge, not two. It is wasted work and duplicated log
lines, not duplicated money.

Closing the window properly needs coordination this slice does not have: the mode held in the root
database with a generation counter, and replicas that drain and hand over rather than switching
independently. Worth doing before the flag is ever flipped on a fleet that cannot take a full
restart.

Once the queue is trusted, the sweep's interval can be lengthened
(`Subscription:ReconciliationPollSeconds`) so it becomes a genuine repair pass rather than a second
scheduler. Removing it is a separate decision and needs its own review.

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

Every transition logs the item id, work type, occurrence key, hashed tenant/aggregate/organization,
correlation and operation ids, lease id, attempt count, and duration. An idle pass logs queue depth
and the oldest due age per work type, which is the shape that shows a queue that is not draining.

Dead-lettered work logs at **error** — it is the one outcome nothing else will pick up — and is
listable through `ListDeadLetteredAsync`.

## Not in this slice

- Payment-module work types (reconciliation, webhook recovery, provider refresh, cleanup). The
  ticket lists them; they belong to `PaymentBackgroundWork` and a separate producer set.
- Per-aggregate producers at the point of state change. Today the repair sweep is the only producer,
  which is why it still walks the roster — just without executing anything. The entity already
  carries `AggregateId`, so a renewal can schedule its own next occurrence when those land.
- An operator endpoint for requeueing dead letters. They are queryable; requeueing is manual.
