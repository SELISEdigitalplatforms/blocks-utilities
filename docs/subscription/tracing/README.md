# Tracing a subscription in production

A customer reports a problem. This is how you find out what happened.

---

## The two stores, and what each one answers

| | Answers | Where |
| --- | --- | --- |
| **Audit trail** | *Who did what, when, and what state resulted* | `GET /api/subscriptions/{id}/audit` |
| **Structured logs** | *How the code got there* — stages, attempts, exceptions, timings | Your log sink |

Both are required. Audit records are append-only with no TTL and no update or delete path, but they
are *intentionally too small to debug execution*. Logs are mutable and retention-bound but carry
everything the audit trail deliberately leaves out.

**The join key is `CorrelationId`.** Every audit event carries it, and every log line inside a
correlated flow carries it too.

---

## The three-step trace

### 1. Get a starting key

| You have | Do this |
| --- | --- |
| The customer quoted a reference | That is the `X-Correlation-ID` response header — go straight to step 3 |
| A subscription id | Step 2 |
| Only an organization | Find the subscription first, then step 2 |

Every API response echoes `X-Correlation-ID`, including endpoints that answer with something other
than the standard envelope — webhooks and the checkout return among them — *"so a caller reporting
a problem can quote it."* Surfacing it in your own error UI makes most support calls one step
shorter.

### 2. Read the audit timeline

```
GET /api/subscriptions/{subscriptionId}/audit?limit=100
```

Organization-scoped and sanitised; actor and payment identifiers stay in the database. Each event
carries:

```
OperationId   CorrelationId   Operation   Stage   Outcome   Source
AmountMinor   CurrencyCode    FromStatus  ToStatus
ErrorCode     FailureKind     Attempt     OccurredAtUtc
```

Read down the timeline to the step that went wrong and take its **`CorrelationId`**.

### 3. Grep the logs by that correlation id

```
CorrelationId="<the value from step 2>"
```

That returns the whole flow — API request, any queued work it produced, and the provider calls that
finished it.

---

## What the log lines carry

Identifiers are written **in clear, not hashed**. That is deliberate: hashing them *"was the reason
the logs could not be followed"* — an operator holding an id from the database or the console had no
way to reach its log lines without recomputing a digest by hand. Personal data (a shopper's email,
name, phone) still goes through the hash.

Background work runs inside a log scope that stamps **every line it writes**:

```
WorkItemId  WorkType  WorkKey  TenantId  SubscriptionId
OrganizationId  CorrelationId  OperationId  LeaseId  AttemptCount
```

So a message like `Subscription work completed DueAtUtc=… DurationMs=2 LagSeconds=20` is not missing
its context — the template just doesn't repeat what the scope already carries. Query the fields, not
the message text.

---

## The trace id column, and why the worker's used to be empty

The bracketed value beside each line is the **trace id**, not the log scope. The platform's log
pipeline enriches every record from `Activity.Current`, so a line written outside any activity
carries an empty one:

```
utilities         [8ced109d33cc2f17b42e8ae1cfc40e9e]  Executing endpoint '…'
utilities-worker  []                                  Subscription work completed …
```

The API is instrumented per HTTP request, which is where its activity comes from. **A worker serves
no request**, so nothing was creating one and every line it wrote had nothing to be stamped with.
The enricher was working correctly and had nothing to read.

Background work now runs inside a span of its own — `subscription.work {WorkType}`, kind `Consumer`
— started by `SubscriptionWorkDispatcher` and carrying:

```
subscription.work.type   subscription.work.item_id   subscription.work.attempt
subscription.tenant_id   subscription.subscription_id   subscription.correlation_id
```

So worker lines now carry a trace id, and every line of one attempt shares it.

> **The source has to be subscribed to.** `Blocks.Subscription.BackgroundWork` — the same name the
> queue's meter uses — is registered in `server/Worker/Program.cs`. Starting an activity from a
> source nothing listens to returns null and sets nothing current, exactly as recording to a meter
> no exporter asked for records nothing. No exporter is named at that registration on purpose: the
> platform's own tracing setup owns where spans go.

---

## Joining a worker attempt to the request that caused it

Work scheduled from inside a request stores that request's W3C trace context on the queue item. When
the attempt eventually runs, it reports it three ways:

| Where | What |
| --- | --- |
| Span link | `ActivityLink` to the scheduling trace |
| Span tag | `subscription.scheduled_by.trace_id` |
| Log scope | `ScheduledByTraceId` on every line of the attempt |

So an operator holding the trace id of the request a customer complained about can find the
background work it caused — by following the link if the backend renders links, and by grepping the
trace id if it does not.

**It is a link, not a parent, and that is deliberate.** A renewal is scheduled a month before it
runs and a cancellation up to a year. A span that made itself a child of the request that scheduled
it would describe a single trace as lasting a year — past every backend's retention window, and not
something anybody can open. The link says the same causal thing without lying about duration.

`ScheduledByTraceId="none"` means nothing scheduled it from inside a request or a sweep pass —
anything queued at startup, for instance.

The repair sweep runs each tenant pass inside a `subscription.repair_sweep` span, so its own
announcement lines carry a trace id and the items it queues link back to the pass that found them.
That is the answer to "why does this work exist?" when nothing a customer did explains it.

The parent is whatever activity is ambient at dispatch — nothing in the worker loop, so an attempt
there is a root span. When due jobs are run on demand from the admin endpoint instead, the attempt
belongs to that request's trace, which is what somebody watching that request wants to see.

---

## Reading sweep lines

Lines from the repair sweep look like this, and they are **not** about any one customer:

```
Repair sweep announced subscription work AnnouncedCount=1 WorkKey="sweep20260902T2035Z"
  CorrelationId="sweep-…" CorrelationOrigin="MintedByRepairSweep" TenantId="…"

Scheduled subscription work WorkType=ActivationRecovery WorkKey="sweep20260902T2035Z"
  TenantId="…" SubscriptionId="none" DueAtUtc=… CorrelationId="sweep-…"
```

Two things to know:

- **`SubscriptionId="none"` means the work is tenant-wide**, not that an id was lost. Work about one
  subscription carries its real id; `missing` would mean something genuinely absent.
- **`CorrelationOrigin="MintedByRepairSweep"` means the trail stops here going backwards.** The sweep
  minted this correlation itself, so there is no upstream customer action to find. Anything
  downstream carries it forward. These lines are the sweep noticing work needs doing — they are
  normal, and they are not a customer's problem.

---

## Alert on this marker

```
SUBSCRIPTION_AUDIT_WRITE_FAILED
```

Audit writes are deliberately **fail-open** after a business operation: an unavailable audit store
never makes a caller retry money movement. So the money is right and the trail has a hole. Alert on
the marker; the payment and subscription ledgers remain the reconciliation source.

---

## Common questions, and where the answer lives

| Question | Look at |
| --- | --- |
| "Why was I charged this amount?" | Audit event's `AmountMinor` + the settlement breakdown on the payment record |
| "Why did my card fail?" | Audit `ErrorCode` / `FailureKind` / `Attempt`, then logs by correlation id |
| "Why did my subscription stop working?" | Audit `FromStatus` → `ToStatus`; `Unpaid` grants nothing |
| "Where did my allowance go?" | The usage ledger — append-only, never expires, corrections are reversal entries |
| "My overage bill is wrong" | Usage invoice for that `PeriodKey`; rating is graduated from the first overage unit |
| "Nothing happened at all" | Check whether the work was ever scheduled — grep `WorkKey` |

The usage ledger is the one to reach for on any billing dispute: it is append-only and never
expires, so a bill can always be explained even after counters have aged out.
