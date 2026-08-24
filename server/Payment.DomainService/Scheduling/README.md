# Payment background work scheduler

A durable queue for the payment module's recovery work, held in the platform's root database and
drained by the worker. This is the payment counterpart of `Subscription.DomainService/Scheduling`,
and deliberately its near-twin — see [Extraction](#extraction) for why the duplication is temporary
and which direction it collapses in.

## Why

`PaymentReconciliationBackgroundService` — the safety net that was supposed to recover a payment
whose provider call succeeded but whose local write or dispatch was lost — **has its loop commented
out**. It starts, logs `Payment reconciliation safety net is DISABLED`, and returns. So today
nothing recovers such a payment: it sits in a non-terminal state until somebody notices it by hand.

Turning this scheduler on restores that recovery rather than relocating it. Leaving it off changes
nothing, because there is nothing running to change.

The second reason is the one the subscription side had too: the sweep it replaces walked every
tenant on every pass, querying each tenant's database whether or not anything was due. Claiming from
this queue is one indexed query against one collection, and cost stops tracking tenant count.

## Shape

| Piece | Job |
| --- | --- |
| `PaymentBackgroundWork` | The work item. Ids, timing, lease, attempts. No card data, no secrets, no payloads. |
| `PaymentWorkQueue` | The database. Indexes, atomic claim, lease renewal, completion, backoff, dead-letter. |
| `PaymentWorkScheduler` | Producing. One place decides priority and occurrence keys. |
| `PaymentBackgroundWorkDispatcher` | Claiming, running, the lease watchdog, the outcome. |
| `PaymentWorkHandlers` | Five thin handlers, one per work type, each delegating to the existing processor. |
| `PaymentWorkMetrics` | Nine instruments on `Blocks.Payment.BackgroundWork`. |
| `PaymentWorkTenantSource` | The roster the producing pass walks. |
| `PaymentSchedulerMode` | The on/off answer, captured once per process. |

Handlers are thin on purpose. Every processor already re-reads the tenant's own state, decides what
is still due, and derives its provider idempotency from persisted identity. Re-deciding any of that
here would give the same money two sets of rules.

## The two databases

Scheduling lives in `BlocksRootDb`; the money lives in each tenant's database; there is no
transaction across them. What holds them together is that the queue is never the authority on
anything financial — an item says *look at this*, never *this happened*. Three failure modes follow,
and all three are survivable:

- **Item written, work never runs.** The item stays pending and is claimed by the next pass.
- **Work runs, completion never written.** The lease expires, the item is reclaimed, and the handler
  re-reads tenant state to find the work already done — the provider's idempotency record and the
  local state agree, so nothing is charged twice.
- **Item lost.** Recovery is delayed, not lost: the producing pass re-announces the occurrence on
  the next bucket.

## Rollout

`Payment:SchedulerEnabled` is **off** by default, and nothing in `appsettings*.json` sets it.

- **Off** — nothing runs. This is today's behaviour, disabled reconciliation included.
- **On** — one worker service both produces and drains.

**Enabling requires a full fleet restart, not a rolling one.** The mode is captured once per process
by `PaymentSchedulerMode`, so it cannot change under a running loop; it is *not* consistent across a
fleet, and nothing in this process can detect a replica running the other way. Every worker logs its
mode at warning on startup — `mode: DISABLED` or `mode: QUEUE`.

1. Merge and deploy with `SchedulerEnabled` off.
2. Confirm every replica is on the new version.
3. Stop all worker replicas.
4. Set `SchedulerEnabled=true`.
5. Start the whole fleet.
6. Confirm from the logs that every replica reports `mode: QUEUE`, that indexes were established,
   and that depth is draining rather than growing.

Unlike the subscription switch, a mixed fleet here is comparatively benign: the mode this replaces
is *nothing running*, so a replica still on the old setting recovers nothing rather than recovering
the same payment twice. Two queue-mode replicas racing the same item are held apart by the lease,
and two attempts at the same recovery converge through provider idempotency.

Indexes are a gate, not a warning: `WaitForIndexesAsync` retries and refuses to claim until they
exist. The unique occurrence index is what makes producing idempotent, and a duplicate written
before it exists is one the index can never afterwards be built over — the hole would hold itself
open.

## Producing

`PaymentWorkSchedulerBackgroundService` both produces and drains. There is nothing to hand over
from: with the old sweep dead, a second service scheduling for this one would be two new things
where one will do.

Producing walks the tenant roster and announces one occurrence per work type per tenant per
five-minute bucket — bucketed rather than per-pass, so that a pass overlapping itself, or two
workers on the same roster, produce one item rather than one each. That walk is the very cost the
queue exists to remove, and it survives here only on the producing side: claiming, which is what a
stuck payment actually waits on, is one indexed query. Producers at the point of state change would
remove the walk entirely, and belong beside the code that writes payments.

Every work type is announced rather than only those with work waiting, because deciding that here
would mean the per-tenant queries this exists to avoid. An occurrence with nothing to do costs one
claim and one completion.

## Priority

Lower runs first.

| Work type | Priority |
| --- | --- |
| `PaymentRecovery` | 10 |
| `RefundRecovery` | 20 |
| `CaptureRecovery` | 30 |
| `OutboxPublication` | 60 |
| `RefundOutboxPublication` | 70 |

Money the payer is owed, or has already paid, comes before the records of it. Ordered by age alone,
a backlog of outbox events would delay a refund.

## Settings

All under `Payment:`.

| Setting | Default | Meaning |
| --- | --- | --- |
| `SchedulerEnabled` | `false` | Whether the queue runs at all. |
| `SchedulerPollSeconds` | `10` | Idle poll interval. A busy worker goes straight to the next batch. |
| `SchedulerBatchSize` | `20` | Items claimed per pass. |
| `SchedulerMaxParallelism` | `4` | Items run at once. Bounded: this work talks to a payment provider. |
| `SchedulerLeaseSeconds` | `120` | How long a claim holds an item. |
| `SchedulerMaxAttempts` | `5` | Attempts before dead-lettering. |
| `SchedulerRetryBaseSeconds` | `30` | Backoff base, doubled per attempt, jittered. |
| `SchedulerRetryMaxSeconds` | `3600` | Backoff cap. |
| `SchedulerCompletedRetentionDays` | `14` | How long completed records are kept. |
| `SchedulerBucketMinutes` | `5` | The occurrence bucket the producing pass writes into. |

## Retention

The TTL index is on `PurgeAtUtc`, which is set **only on completion**. A TTL index ignores documents
whose field is absent, so pending, processing and dead-lettered records are never removed
automatically — unfinished work, and work somebody must look at, both stay.

## Leases

A claim holds an item for `SchedulerLeaseSeconds`. The dispatcher renews at half the lease and
tracks a *confirmed* expiry: a renewal call that fails is not proof the lease is gone, but time
passing is, so a handler is cancelled once the last confirmed expiry has passed. The renewal runs on
an independent timer awaited alongside the handler, which is what makes a renewal call that never
answers survivable — waiting on it instead would let the lease expire while the handler ran on, with
another worker free to reclaim and repeat the same work.

An attempt that has lost its lease writes nothing at all. Neither completion nor failure: whoever
holds the item now decides it, and writing either from here would overwrite their outcome with a
stale one.

## Metrics

Nine instruments on the `Blocks.Payment.BackgroundWork` meter: items scheduled, claimed, completed,
retried, dead-lettered, leases lost, handler duration, and gauges for queue depth and oldest due
age. Tagged by work type and error **code** only — never by tenant, item or message, which is what
keeps cardinality bounded. The gauges report the last idle-pass reading, because depth is an
aggregation over another database and a collector should not decide when that runs.

## What is missing

**There is no audit trail.** The subscription module records who requeued or abandoned a piece of
work and why, as a business fact kept separately from its logs. The payment module has no equivalent
to record against, so the dispatcher writes log lines only, and there are no operator recovery
endpoints here. A dead-lettered payment recovery is therefore visible in the queue and in the logs,
and nowhere that answers "who decided this, and on what grounds" months later. That is a real gap
rather than a simplification, and worth closing before an operator is asked to act on this queue.

## Extraction

This directory is close to a copy of the subscription scheduler: the queue mechanics, the dispatcher
and the lease watchdog differ only in their type names. That is duplication on purpose and for now —
the two arrived in separate pull requests, and merging one into a shared abstraction the other did
not yet have would have blocked each on the other.

Once both are merged, the mechanics can move here, into `Payment.DomainService`, and the
subscription module can take a dependency on them. That direction, and only that direction:
`Subscription` already references `Payment` (its gateway, its failure kinds, its `PaymentLogValue`),
and `Payment` references nothing of `Subscription`. Inverting it, or introducing a third shared
project, would create a cycle or a project whose only reason to exist is to avoid one.

What moves: `PaymentWorkQueue`'s claim, lease, backoff and dead-letter mechanics, the dispatcher's
lease watchdog, and the metric shapes. What does not: work types, priorities, handlers and
producers, all of which are about what a module means by work rather than about queueing it.
