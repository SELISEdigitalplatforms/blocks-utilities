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

The scheduler also **refuses to claim anything until the queue's indexes exist**, retrying with
backoff and logging at error each time. Without the unique occurrence index, producing is not
idempotent — two producers can create two items for one billing period. Draining a queue that may
hold duplicates is worse than draining nothing: nothing is visible and recoverable, a double charge
is neither.

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

A held lease is renewed at half the lease for as long as its handler runs, so work that outlives its
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
