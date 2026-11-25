# Subscription charges that fail after they were applied

Issues: #681 (Adyen upgrade authorized but never captured), #806 (capture via API), #807 (cancel authorized payment via API).

## Intent

A subscription charge (renewal, plan upgrade, added seats, usage overage) is applied the moment the provider accepts the charge request. With Adyen on **manual capture** the money is only held at that point; it is collected when the client captures through `POST /payments/{id}/captures`, and the hold lapses after a few days if nobody does. Today nothing on the subscription side notices: the subscriber keeps what the charge was for and the merchant is never paid.

Geo wants both capture modes for subscription charges, chosen per client through the provider's existing `ManualCapture` setting. So the subscription module must cope with a charge whose money arrives later, or never.

## Decisions (Geo, 2026-11-25)

1. **Capture mode.** Per client, through the provider configuration. Nothing new; subscription charges already follow it.
2. **The client captures** held subscription charges through the API. It finds them with `GET /payments?paymentStatuses=AUTHORIZED` or the `PaymentAuthorized` lifecycle event.
3. **A charge that fails after it was applied** (hold expired, cancelled, or declined late) keeps the change and retries:
   - Renewal: past due, retry, access kept while retrying.
   - Plan upgrade: keep the new plan, retry the upgrade amount.
   - Added seats: keep the seats, retry the seat amount.
   - Usage overage: retry the invoice (existing `UsageRatingMaxAttempts` cadence; never affects the subscription's status).
4. **After the retries** (`DunningMaxAttempts`, default 4, `DunningRetryIntervalHours` apart): the subscription goes **Unpaid** and access stops, exactly as a renewal that never pays does today. A card added later reopens it through the existing Unpaid recovery.
5. **Cancelling a hold through the API (#807)** that belongs to a subscription charge is treated as the charge failing: same retry path.
6. **Invoice.** When the money is finally not collected: the invoice is marked unpaid and a credit note is issued against it.
7. **Notifications.** Customer and admin, when a charge fails after being applied and again when the subscription goes Unpaid.

## Non-goals

- Reverting an applied upgrade or removing seats. Decided against: entitlements, usage snapshots and the issued invoice all moved when the change applied.
- Waiting for capture before applying a change. Geo wants the change live immediately.
- Teaching the payment module about subscriptions. The dependency stays one-way.

## Design

### 1. Noticing that a charge failed

No consumer of the payment lifecycle topic exists inside utilities (`PaymentLifecycleEventConsumer` is a no-op), and the module already watches payments by scheduling its own work and reading the payment record (`ActivationRecovery`, `SettlementReservationRecovery`). Same pattern:

- New work type `ChargeSettlementWatch`, scheduled once per applied charge from the four places a charge is applied: `SubscriptionPlanChangeService.ApplyAsync`, `SubscriptionQuantityChangeService.PromoteAsync`, `SubscriptionRenewalService.ApplySuccessAsync`, `SubscriptionUsageRatingProcessor` after `TryMarkChargedAsync`. Key: `settlement-watch:{paymentDetailId}`.
- The handler reads the payment:
  - `Captured`, `PartiallyCaptured`, `Refunded`, `PartiallyRefunded` → done.
  - `Expired`, `Cancelled`, `Refused`, `MakePaymentFailed` → §2.
  - anything else (`Processing`, `Authorized`) → reschedule with growing delay (1h, 4h, 24h, then daily) up to `ChargeSettlementWatchMaxDays` (default 14; Adyen holds lapse before that). Past the limit: log and stop, the watch is best effort.
- Idempotent: the handler is a pure read until the payment is terminal, and §2's write is guarded.

Stripe charges are recorded `Captured` on creation (`StripeInvoiceBillingGateway.RecordSettlementAsync`), so the watch ends on its first run there.

### 2. Reacting

One new piece of state on `SubscriptionDetail`:

```
OutstandingSettlement { SettlementId, Kind (Renewal|PlanChange|QuantityIncrease), AmountMinor,
                        CurrencyCode, Breakdown, FailedPaymentDetailId, FailedAtUtc, AttemptCount }
```

Written by the watch handler when a renewal, plan-change or seat charge fails, in the same versioned write that moves the subscription to `PastDue` (`SubscriptionRenewalService.ApplyFailureAsync` already does the PastDue transition and attempt bookkeeping for renewals; it is extended to take the outstanding settlement rather than only a period). Rules:

- A subscription holds at most one outstanding settlement. A second failure while one is open is added to its amount, not queued. Amounts are what the failed payment was for (`PaymentDetail.PreciseAmount` and its `SubscriptionInvoiceBreakdown`), so no re-pricing.
- The existing dunning retry (`RenewalWorkHandler` → `RenewAsync`) charges the outstanding settlement when one is present instead of the period fee, keyed `settlement:{SettlementId}:{attempt}` through the same `ISubscriptionBillingGateway`. Success clears it, restores `Active`, and schedules a new watch for the new payment. Each failure is a dunning attempt; the last moves the subscription to `Unpaid` via the existing `MoveToUnpaidAsync`.
- If the period fee also comes due while a settlement is outstanding, the retry charges the settlement first and, only once that settles, the ordinary renewal runs in the same pass and charges the period. Two charges, two payment records, two invoices: simpler than merging and each has its own watch.
- The outstanding amount survives Unpaid. A card added later recovers through the existing `RecoverAsync` → `RenewAsync`, which collects it before the overdue period.
- Usage overage: the watch handler calls the existing `FailAttemptAsync` on the usage invoice (clears `PaymentDetailId`, schedules `UsageInvoiceCharge`), nothing else. After `UsageRatingMaxAttempts` the invoice is abandoned as today.
- Trialing, cancelled or already-Unpaid subscriptions: record the failure event, no transition (mirrors `ApplyFailureAsync`'s Unpaid branch).

### 3. Cancel via API (#807)

`POST /payments/{id}/cancel` (payment module) reverses the hold through the existing refund gateway's `/reversals` path and the `CANCEL_OR_REFUND` webhook moves the payment to `Cancelled`. The watch in §1 then sees `Cancelled` and runs §2. No subscription-specific code in the endpoint.

### 4. Invoice

When §2 moves the subscription to `Unpaid` (or abandons a usage invoice):

- `FinancialDocumentStatus` gets `Unpaid`; the invoice issued for the failed charge (found by `PaymentDetailId`) is moved there.
- A credit note is issued against it through a new `SubscriptionFinancialDocumentIssuer.IssueUncollectedCreditNoteAsync`, modelled on `IssueRefundCreditNoteAsync` but keyed on the invoice rather than a refund. Same number sequence, same delivery.

### 5. Notifications

`SubscriptionNotificationEmailService.SendAsync` is event-driven. Two new subscription events, `SubscriptionChargeFailed` (with kind and amount) and the existing `SubscriptionUnpaid`, get templates:

- Customer: the billing contact, as every other subscription email.
- Admin: the tenant's notification address (where `PaymentMethodExpiryEmailService` already sends admin mail; reuse its recipient resolution).

Also sent on the first failure, not only on Unpaid, so a client whose capture job stopped sees it within a day.

## Configuration

| Option | Default |
|---|---|
| `ChargeSettlementWatchMaxDays` | 14 |
| `DunningMaxAttempts` / `DunningRetryIntervalHours` | existing, reused |
| `UsageRatingMaxAttempts` / `UsageRatingRetryHours` | existing, reused |

## Delivery

1. Payment side: `EXPIRE` / `CANCELLATION` webhooks (PR #835).
2. Watch + outstanding settlement + retry (§1, §2). Largest piece; ships with the renewal and plan-change paths first, seats and usage in the same PR if small.
3. Invoice status + credit note (§4).
4. Notifications (§5).
5. Cancel endpoint (§3).
6. #806: no code. Document the capture endpoint, permission `blocks-utilities::payment::manage-capture`, and the list query.

## Open questions

- ~~List filter for subscription charges.~~ Resolved: subscription charges and one-off stored-card charges both carry `paymentFlow = RECURRING_CHARGE`; hosted checkout is `HOSTED_CHECKOUT`. Geo captures both kinds, so `GET /payments?paymentStatuses=AUTHORIZED` is the whole query. A subscription charge is recognisable by its `orderId` if they ever need to.
- Admin recipient: confirm with Geo which address the admin notification goes to.
