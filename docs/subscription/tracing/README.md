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

The controller route is `subscriptions/{subscriptionId}/audit`; whether it sits under `/api`
depends on the gateway in front of the service.

| Query parameter | Meaning |
| --- | --- |
| `limit` | Events to return. `0` or absent means 100; anything above 500 is capped at 500. |
| `organizationId` | Only for a console caller looking at another organization's subscription. Everyone else sees their own organization, whatever this says. |

Organization-scoped and sanitised; actor and payment identifiers stay in the database. Each event
carries:

```
EventId       OperationId     CorrelationId   Operation   Stage   Outcome   Source
AmountMinor   CurrencyCode    FromStatus      ToStatus    PriceId  EffectiveAtUtc  Detail
ErrorCode     FailureKind     Attempt         OccurredAtUtc
```

`Detail` is a short summary of what changed, written by the code (`Automatic discount 8% → 15%`).
A price or overage change an admin extended to existing subscribers shows up here as
`PriceTermsScheduled` or `OverageRatesApplied`. To see who changed a **plan** — the plan, its
prices or its overage rates — read `GET /api/subscription-plans/{planId}/audit` instead.

Read down the timeline to the step that went wrong and take its **`CorrelationId`**.

### 3. Grep the logs by that correlation id

```
CorrelationId="<the value from step 2>"
```

That returns the whole flow — API request, any queued work it produced, and the provider calls that
finished it.

---

## What the log lines carry

**The log store keeps the rendered message text, plus `TenantId`, `TraceId` and `SpanId`, and the
log console searches the text.** Structured properties and log-scope fields that do not appear in
the text are dropped on the way in. So search the message text, and only for things that are in it.

`SearchableIdLogSink` (installed in both the API and the worker) makes the ids that matter
searchable anyway. On every line that knows them, it appends whichever of these the line does not
already name:

```
SubscriptionId=…  PaymentId=…  SmsMessageId=…  CorrelationId=…
```

It skips `none` and `missing`. So a line like `Subscription work completed DueAtUtc=… DurationMs=2`
reaches the store as `Subscription work completed DueAtUtc=… DurationMs=2 SubscriptionId=sub-1
CorrelationId=corr-1`, and searching `SubscriptionId=sub-1` or `CorrelationId=corr-1` finds it.

Background work also runs inside a log scope:

```
WorkItemId  WorkType  WorkKey  TenantId  AggregateId  OrganizationId
CorrelationId  ScheduledByTraceId  OperationId  LeaseId  AttemptCount
```

Only `TenantId`, `CorrelationId` and `SubscriptionId` from that scope survive into the store: the
first as a column, the other two through the sink. **The rest are dropped** unless a message names
them itself. Only the operator dead-letter recovery lines name `WorkItemId`. Those fields still
reach any sink that keeps structured properties, and `subscription.work.*` span tags carry the same
facts when spans are exported.

`SubscriptionId` is added to the scope only when the work is about one subscription. Financial
document work (`FinancialDocumentIssue`, `FinancialDocumentDelivery`) is keyed on a payment or a
document instead, so those lines carry `AggregateId` in the scope but no `SubscriptionId`. Find them
by `CorrelationId`.

Identifiers are written **in clear, not hashed**, through `PaymentLogValue.Id`. That is deliberate:
hashing them *"was the reason the logs could not be followed"*. An operator holding an id from the
database or the console had no way to reach its log lines without recomputing a digest by hand.
Personal data (a shopper's email, name, phone) still goes through the hash.

**Not every line has been converted yet.** Some subscription log lines still hash record ids, and
say so in the field name. Searching them by the raw id finds nothing:

| Field | Hashed value | Where it shows up |
| --- | --- | --- |
| `PlanHash`, `PriceHash` | Plan and price ids | Plan catalogue edits: create, edit, archive, tax, discount, amount and overage-rate changes |
| `PurchaseHash` | Credit purchase id | Standalone credit purchases (spec 005) |
| `OrganizationHash` | Organization id | Catalogue, checkout, creation, cancellation, usage recording and billing-profile lines |
| `SubscriptionHash`, `TenantHash`, `PaymentHash`, `BillingAccountHash`, `ActorHash`, `IdHash`, `SourceHash` | As named | A few older lines |

For those, search by `CorrelationId` instead: the sink appends it in clear, and the request's other
lines carry the same one. A field named `…Id` is in clear; one named `…Hash` is not.

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
| Log scope | `ScheduledByTraceId` on every line of the attempt (dropped by the log store — see above) |

So an operator holding the trace id of the request a customer complained about can find the
background work it caused by following the link, or searching the span tag, **in the trace
backend**. The log store does not keep `ScheduledByTraceId`, so there the join is by
**`CorrelationId`**. The work item is created under the request's correlation id, and the sink puts
it on every line of both sides.

**It is a link, not a parent, and that is deliberate.** A renewal is scheduled a month before it
runs and a cancellation up to a year. A span that made itself a child of the request that scheduled
it would describe a single trace as lasting a year — past every backend's retention window, and not
something anybody can open. The link says the same causal thing without lying about duration.

In the trace backend or a structured sink, `ScheduledByTraceId="none"` means nothing scheduled it from inside a request or a sweep pass —
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

- **`SubscriptionId="none"` in the `Scheduled subscription work` line means the work is
  tenant-wide**, not that an id was lost. Work about one subscription carries its real id; `missing`
  would mean something genuinely absent. Once that work runs, its lines carry no `SubscriptionId` at
  all, only the sweep's `CorrelationId` (see the log store section above).
- **`CorrelationOrigin="MintedByRepairSweep"` means the trail stops here going backwards.** The sweep
  minted this correlation itself, so there is no upstream customer action to find. Anything
  downstream carries it forward. These lines are the sweep noticing work needs doing — they are
  normal, and they are not a customer's problem.

---

## Why an email did not arrive

Every email the subscription module hands to the mail queue, or decides not to send, is recorded as
a **mail delivery report** in the `SubscriptionMailDeliveryReports` collection. There is no API for
it; read it in the database. Reports are **kept for 90 days** (`PurgeAtUtc`, a TTL index).

| Field | Use |
| --- | --- |
| `CorrelationId` | The same value the mail carries. Join to the mail module's own records and to the logs. |
| `Source` | `FinancialDocument`, `SubscriptionNotification` (billing-contact and member notices, and credit purchase confirmations), `UsageThreshold`, `PaymentMethodExpiry` |
| `Outcome` | `Published` (the queue accepted it — nothing more), `PublishFailed`, `NotAttempted` |
| `ErrorCode`, `ErrorMessage` | Why it was not attempted or failed, e.g. `billing_email_missing` |
| `SubjectId`, `OrganizationId`, `TenantId` | What the mail was about |
| `Purpose`, `Language`, `To` | What was asked for, and of whom |
| `PayloadJson`, `PayloadHash` | The exact message sent |

`Published` with no email in the inbox means the problem is on the mail module's side: no template
for that **purpose and language**, a template naming a `{{Key}}` the payload lacks, or a recipient
who is not a registered user of the tenant. Look it up there by `CorrelationId`. Every error code and
the per-purpose rules are in [the email reference](../v1/emails/README.md#why-an-email-did-not-arrive).

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
| "I never got the email" | The mail delivery report for that `CorrelationId` — see [above](#why-an-email-did-not-arrive) |

The usage ledger is the one to reach for on any billing dispute: it is append-only and never
expires, so a bill can always be explained even after counters have aged out.
