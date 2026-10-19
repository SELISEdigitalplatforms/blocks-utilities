# Spec 003: Checkout on First Top-Up

**Status:** Draft for review (2026-10-16). Decisions confirmed with the requester on 2026-10-16
unless marked *(author's call)*.
**Date:** 2026-10-16
**Extends:** Spec 002 (Purchasable Meter Credits), which listed "Hosted checkout fallback when no
card is stored" as out of scope.

## Problem

Spec 002 credit purchases only charge a stored card synchronously. When the billing account has no
`DefaultPaymentMethodId`, `SubscriptionCreditService.PriceAsync` refuses with
`subscription_credits_no_payment_method` (`SubscriptionCreditService.cs` ~line 550), before pricing,
for both preview and purchase.

That blocks the prepaid-wallet setup another team's client needs: a hidden $0 plan whose meters
include nothing, disallow overage, and sell `Never`-expiring credits, so usage spends only bought
credits. A $0 plan with no card requirement starts through `SubscriptionCheckoutService.StartWithoutPaymentAsync`,
which collects no card, so the first top-up can never succeed.

Success: on a subscription with no saved card, a credit purchase opens a hosted checkout for the
quoted total that also saves the card. Once the provider confirms payment, credits are granted
exactly as a stored-card purchase grants them, and the card becomes the billing account's default.
Later top-ups charge that card synchronously, as they do today.

## Scope

### In

- Credit purchase on a subscription with no stored card opens a hosted checkout (Stripe and Adyen)
  for the quoted total and saves the card.
- New purchase status `AwaitingPayment`; the response carries `checkoutUrl`.
- Settlement from the signed payment webhook, from a server-side provider read when the buyer
  returns, and from the recovery sweep. Never from the browser redirect.
- A checkout that expires or is declined fails the purchase and releases a held discount.
- One open checkout per subscription.
- The saved card becomes the default, but only if the account still has none.
- Simulation console: the Buy credits dialog follows the checkout flow.

### Out

- Forcing a checkout when a card is already saved (decision 8: not in v1).
- Granting a $0-total purchase (100% code) with no card on file. It keeps refusing with
  `subscription_credits_no_payment_method`, because the card check runs before pricing.
- Automatic refunds. Money taken that cannot be granted goes to `NeedsAttention`, as in PR #695.
- A different invoice for checkout-paid purchases.
- The known spec 002 gap: buying credits for a meter that a pending downgrade will drop. Not
  changed here.

## Current Code This Builds On

| Piece | Where | Used for |
|---|---|---|
| Stored-card purchase, pending record, idempotency replay | `SubscriptionCreditService.PurchaseAsync`, `ReplayAsync`, `FinalizeAsync` | unchanged for accounts with a card; `FinalizeAsync` reused as-is for the grant, invoice, event and threshold re-arm |
| Quote and refusals | `SubscriptionCreditService.PriceAsync` | the no-card refusal becomes a branch; every other refusal (lifecycle, bounds, currency, discount, 50¢ floor) still runs first |
| Hosted checkout that saves a card | `IPaymentService.MakePaymentAsync` → `HostedCheckoutInitiationService` → `StripeInitiationRequestFactory` / `AdyenInitiationRequestFactory` | the same call `SubscriptionCheckoutService.ChargeAsync` makes for a paid signup |
| Card adoption | `SubscriptionActivationProcessor.AdoptProviderCustomerAsync` + `IsAdoptable` → `BillingAccountRepository.TrySetProviderCustomerAsync` | moved into a shared collaborator so both the activation processor and credits call it |
| Webhook fast path | `PaymentWorkCommandConsumer` passes `webhookResult.TransitionedPaymentDetailIds` to `ISubscriptionActivationProcessor.SettleForPaymentsAsync` | a sibling call into the credit service with the same ids |
| Provider read | `ISubscriptionPaymentReconciler` (`StripeCheckoutReconciliationService`, Stripe only) | read on return |
| Crash recovery | `SubscriptionCreditService.RecoverStaleAsync` via `CreditPurchaseRecoveryWorkHandler`; `FindChargeAsync` looks the payment up by `CreditPurchaseChargeKeyFor(purchaseId)` | extended with an `AwaitingPayment` branch; the lookup works unchanged |
| Discount hold | `ICampaignRedemptionRepository.TryReserveAsync` / `TryReleaseAsync` / `TryMarkRedeemedAsync` | unchanged; held for the life of the checkout |

### Why not reuse `SubscriptionPaymentLink`

Signup settles through `SubscriptionPaymentLink` + `SubscriptionActivationProcessor`. A credit
checkout does **not** go through that path:

- `AbandonAsync` expires the subscription (`Incomplete` → `IncompleteExpired`) for every link that
  is not a card setup. An abandoned top-up would end the customer's subscription.
- `ISubscriptionPaymentLinkRepository.FindBySubscriptionAsync` returns *any* link for a subscription
  (`FirstOrDefault`, unordered). `StartPaymentMethodSetupAsync`, `GetPendingCheckoutUrlAsync` and the
  activation `RecoverStaleAsync` all read it as "the signup link". A second link per subscription
  breaks those readers.
- `SubscriptionCreditPurchase` already has the order id, charge key, status and sweep that a link
  would duplicate.

So the purchase record is the settlement record, and the payment's idempotency key ties the
payment to it.

## Decisions

| # | Decision | Status |
|---|---|---|
| 1 | New status `AwaitingPayment`; the purchase response carries `checkoutUrl`. | Confirmed |
| 2 | Credits are granted only after the provider confirms: by the signed webhook, or by a server-side provider read when the buyer comes back (Stripe: `ISubscriptionPaymentReconciler`). The browser redirect is never trusted. Adyen has no reconciler, so on Adyen only the webhook or sweep can confirm. | Confirmed (webhook plus read on return) |
| 3 | An expired, cancelled or declined checkout moves the purchase to `Failed` (`subscription_credits_checkout_expired` / `_declined`) and releases a held discount. The same key then replays `Failed`; the client retries with a new key. | Confirmed |
| 4 | The same idempotency key while the checkout is open returns the same `checkoutUrl` and never opens a second session. The payment is raised under `CreditPurchaseChargeKeyFor(purchaseId)`, so `MakePaymentAsync` hands back the existing payment even on a race. | Confirmed |
| 5 | Paid, but the subscription ended (`Canceled` / `IncompleteExpired`, the same test PR #695 uses) before settlement: no grant; purchase → `NeedsAttention` for an operator to refund. | Confirmed (NeedsAttention, no auto-refund) |
| 6 | `ExpiresAtUtc` for a Reset (Periodic) grant stays fixed at quote time, as today. If that moment has already passed when the payment is confirmed, the purchase goes to `NeedsAttention` instead of granting credits that are expired on arrival. | Confirmed (at quote). The dead-on-arrival guard is *(author's call)* |
| 7 | The saved card becomes the billing account default **only if the account still has no `DefaultPaymentMethodId`**. A card added meanwhile is never replaced. Adoption uses the existing `IsAdoptable` rule, which already accepts Adyen cards without a payer reference (fix for "hasPaymentMethod=false beside an ACTIVE card"). | Confirmed (default only if none) |
| 8 | Caller-forced checkout when a card is saved. | Out of v1 |
| 9 | The 50¢ floor (`subscription_credits_below_minimum_charge`, `MinimumSettlementChargeMinor`) applies to the checkout total. It already runs in `PriceAsync` before the no-card branch, so nothing new is needed. | Confirmed |
| 10 | A second purchase with a different key while one checkout is open on the subscription: 409 `subscription_credits_checkout_in_progress`, with the open `checkoutUrl` in the error details. | Confirmed |
| 11 | Checkout session lifetime: 60 minutes on both providers. | Confirmed |
| 12 | Checkout-paid purchases get the same invoice as stored-card ones. | Confirmed |
| 13 | Adyen parity in v1. | Confirmed |
| 14 | A confirmation arriving after the purchase was failed for expiry (late Adyen webhook): purchase → `NeedsAttention`, no grant. The code was already released and the buyer may have retried, so granting automatically could double-sell a one-use code. | *(author's call)* |
| 15 | The confirmed amount and currency must equal the purchase's `TotalMinor` and `CurrencyCode`; otherwise → `NeedsAttention`. | *(author's call)* |

## Domain Model

### `CreditPurchaseStatus`

Add `AwaitingPayment = 5`: the hosted checkout is open and no provider decision has arrived yet.
Appended after the existing values so stored integers keep their meaning.

Lifecycle for a checkout purchase:

```
Pending ──open session──▶ AwaitingPayment ──confirmed──▶ Charged ──FinalizeAsync──▶ Finalized
   │                           │
   │                           ├─ expired / declined / cancelled ─▶ Failed (discount released)
   │                           ├─ confirmed, subscription ended ──▶ NeedsAttention
   │                           ├─ confirmed, Reset expiry passed ─▶ NeedsAttention
   │                           └─ confirmed, amount mismatch ─────▶ NeedsAttention
   └─ session could not be opened ─▶ Failed (discount released)
```

`Failed` + later confirmation → `NeedsAttention` (decision 14).

### `SubscriptionCreditPurchase` additions

| Field | Notes |
|---|---|
| `PaymentPath` | `StoredCard` (default; every existing record) / `Checkout` |
| `CheckoutUrl` | provider redirect URL; returned on replay while `AwaitingPayment` |
| `CheckoutExpiresAtUtc` | from the payment's `ExpirationDate` or `HostedCheckoutSessionResponse.ExpiresAt`; the sweep fails the purchase after this time plus a grace period |
| `CardAdoptedAtUtc` | set once the card is adopted or adoption is skipped because a default already exists; null keeps the sweep retrying adoption |

`PaymentId` is filled when the session opens (the hosted payment's id), not only after a charge.
`StoredPaymentMethodId` stays null for checkout purchases. `ReplayChargeAsync` already returns null
(hold) when it is empty, so the Stripe replay branch of the sweep never charges a checkout purchase
by mistake. The sweep branch below still handles checkout purchases separately.

### Index

Add `(TenantId, SubscriptionId, Status)` on `SubscriptionCreditPurchase` for the one-open-checkout
check (decision 10). Add `(TenantId, PaymentId)` for the webhook fast path.

## Flow

### Purchase (`PurchaseAsync`)

1. Validation, `LoadAsync`, idempotency replay: unchanged. Replay of an `AwaitingPayment` record:
   - Stripe: call `ISubscriptionPaymentReconciler.TryReconcileAsync(tenant, paymentId)`. If it
     decided, run the settlement below and answer with the result.
   - Then answer `Success` with `status = AwaitingPayment`, the same `checkoutUrl`, `replayed = true`.
2. `PriceAsync`: every refusal before the card check runs unchanged (lifecycle, meter, not for sale,
   currency, bounds, seat). The no-card check becomes an output (`Account.DefaultPaymentMethodId`
   empty → `Checkout` path) instead of a refusal, **except** when the quoted `TotalMinor` is 0,
   which keeps refusing with `subscription_credits_no_payment_method`. The check moves after the
   quote for that reason. Discount resolution and the 50¢ floor run before it.
3. Checkout path only: refuse with 409 `subscription_credits_checkout_in_progress` if another
   purchase on the subscription is `AwaitingPayment` (or `Pending` with `PaymentPath = Checkout`).
4. Insert the purchase (`Pending`, `PaymentPath = Checkout`) and reserve a one-use discount, exactly
   as today.
5. `IPaymentService.MakePaymentAsync` with the fields `SubscriptionCheckoutService.ChargeAsync` uses:
   provider and `ExpectedProviderId` from the billing account (frozen at signup), `OrganizationId` =
   `ResolveProviderOrganizationId`, `CustomerOrganizationId` and `PaymentMethodOwnerOrganizationId` =
   subscriber, `CustomerEmail`, `OrderId` = `purchase.OrderId`, `Amount` = `TotalMinor` converted back,
   `SavePaymentMethod = true`, `RecurringModel = PaymentConstants.SubscriptionRecurringModel`.
   Idempotency key: `CreditPurchaseChargeKeyFor(purchase.ItemId)`.
   - `RecurringModel = Subscription` is required, not optional: later top-ups charge the stored card
     with `RecurringProcessingModel = "Subscription"` (`RecurringChargeBillingGateway`), and on Adyen
     it is also what switches `StorePaymentMethodMode` from `askForConsent` to `enabled`, so the card
     is always stored.
   - The `ResolvedProviderId` defense-in-depth check from `ChargeAsync` applies too.
6. Session opened → `AwaitingPayment` with `PaymentId`, `CheckoutUrl`, `CheckoutExpiresAtUtc`. Audit
   `CheckoutStarted`. Respond `Success` with `status = AwaitingPayment` and `checkoutUrl`.
7. Session not opened (provider failure, misconfiguration, `payment_method_removal_in_progress`) →
   release the discount, `Failed` with the provider's code, return the failure.

### Checkout lifetime (decision 11)

`MakePaymentRequest` gains an optional `CheckoutExpiresInMinutes`. Stripe: the factory sends
`expires_at = now + minutes` (Stripe requires 30 minutes to 24 hours; today no `expires_at` is
sent, so sessions live Stripe's default 24 hours). Adyen: `AdyenInitiationRequestFactory` sends
`expiresAt = now + minutes`. Today it sends none, and Adyen's own session default is 1 hour.
Sending it makes the lifetime explicit and gives `CheckoutExpiresAtUtc` a value we set ourselves.
Credits pass 60. Every other caller passes nothing and keeps today's behavior.

### Settlement (`SettleCheckoutAsync`, one implementation for all three triggers)

Triggers: the webhook fast path, a replay with the same key, and the recovery sweep. The first
two only act on a decided payment. Only the sweep handles time.

1. Load the purchase by `PaymentId`, require `PaymentPath = Checkout`.
2. Read the payment. Confirmed = status in `Authorized` / `Captured` / `PartiallyCaptured`
   **and** `WebhookConfirmedAtUtc` set (the activation processor's `IsConfirmed` rule; a reconciler
   read is applied "through the same path a real webhook uses", so it counts).
3. Declined (`Refused` / `Cancelled` / `MakePaymentFailed` / `Expired`) → release discount, `Failed`.
4. Confirmed:
   - Purchase already `Failed` → `NeedsAttention` (decision 14).
   - Amount or currency differs from the quote → `NeedsAttention` (decision 15).
   - Subscription missing, `Canceled` or `IncompleteExpired` → `Charged`, then `NeedsAttention`
     (decision 5; the same `ended` test as `RecoverAsync`).
   - Reset grant whose `ExpiresAtUtc` ≤ now → `Charged`, then `NeedsAttention` (decision 6).
   - Otherwise → `Charged` (compare-and-set on `Version`), then `FinalizeAsync` unchanged: grant
     (id derived from the purchase, so a repeat finds it), mark the discount redeemed, invoice via
     `AnnounceCreditPurchaseAsync` with the checkout's payment id (decision 12),
     `SubscriptionCreditsPurchased`, threshold re-arm, `Finalized`.
5. After a confirmed payment (granted or not), adopt the card (decision 7). Failure does not undo
   the grant. `CardAdoptedAtUtc` stays null and the sweep retries.

### Card adoption (decision 7)

Move `AdoptProviderCustomerAsync` and `IsAdoptable` out of `SubscriptionActivationProcessor` into a
shared collaborator used by both callers. Signup behavior is unchanged.

For credits, use a conditional write: a new `IBillingAccountRepository.TrySetProviderCustomerIfNoDefaultAsync`
(filter `DefaultPaymentMethodId` null or empty). It sets `DefaultPaymentMethodId`, `ProviderCustomerId`
(when the method has one) and `ProviderOrganizationId`, the same fields as `TrySetProviderCustomerAsync`.
If a default already exists, mark `CardAdoptedAtUtc` and do nothing else.

Adyen: `IsAdoptable` accepts an Adyen card with no `ProviderPayerReference`, so the past bug does not
recur. The card is looked up by `payment.ShopperReference` + **subscriber** organization, never the
merchant scope (see the remarks in `AdoptProviderCustomerAsync`). Adyen's token can arrive in a later
notification than the authorisation, so the first adoption attempt may find no card. That is why
adoption retries independently of the grant.

### Recovery sweep (`RecoverStaleAsync`)

Add `AwaitingPayment` to the statuses listed. Branch on `PaymentPath = Checkout` before the existing
`Pending` logic:

| State | Action |
|---|---|
| `Pending`, checkout, older than `CreditPurchaseRecoveryAfterSeconds` | `FindChargeAsync` by charge key. No payment → release discount, `Failed` (session never opened). Payment found → adopt it: `AwaitingPayment` with its id and URL. |
| `AwaitingPayment`, payment decided | `SettleCheckoutAsync` |
| `AwaitingPayment`, undecided, before `CheckoutExpiresAtUtc + CreditCheckoutExpiryGraceMinutes` | Stripe: try the reconciler; otherwise leave it |
| `AwaitingPayment`, undecided, past that time | Stripe: reconciler first (it marks an unused expired session declined). Still undecided → release discount, `Failed` `subscription_credits_checkout_expired`. |
| `Finalized`/`NeedsAttention`, checkout, `CardAdoptedAtUtc` null, younger than `CreditPurchaseAlertAfterHours` | retry adoption |
| `Charged` | unchanged (finalize, or hold if ended) |

New option `CreditCheckoutExpiryGraceMinutes` (default 15), in `SubscriptionOptions` next to the
existing credit timings.

## API Contract

No new endpoints. Changes to `POST subscriptions/{id}/credits` and `.../credits/preview`:

| Change | Detail |
|---|---|
| Preview without a card | succeeds (was 409 `subscription_credits_no_payment_method`); the quote gains `paymentPath: "Checkout" \| "StoredCard"` so the UI can say "you'll be redirected to pay" |
| Purchase without a card | 200 with `purchase.status = "AwaitingPayment"`, `purchase.checkoutUrl`, `grant = null`, `documentId = null` |
| Replay while open | 200, same `checkoutUrl`, `replayed = true` (after a Stripe provider read) |
| Replay after settlement | as spec 002: the finalized purchase and grant, or the failure |
| Second checkout | 409 `subscription_credits_checkout_in_progress` |
| $0 total, no card | 409 `subscription_credits_no_payment_method` (unchanged) |
| ≤ 50¢ | 400 `subscription_credits_below_minimum_charge` (unchanged, applies to checkout) |

`CreditPurchaseResponse` gains `checkoutUrl` (string?, only while `AwaitingPayment`) and
`paymentPath`. `GET .../credits/purchases?status=AwaitingPayment` works through the existing filter.
OpenAPI under `server/Api/Documentation/subscription` is updated.

Authorization: unchanged from spec 002 (`blocks-utilities::subscription::manage`).

## Edge Cases & Failure Modes

- **Browser returns before the webhook**: the client re-POSTs the same key. On Stripe that runs a
  provider read and may grant. Otherwise the response stays `AwaitingPayment` and the client polls.
- **Buyer closes the tab**: the webhook still settles. Expiry fails it via the sweep.
- **Two tabs, same key**: one purchase document (id from key), one payment (charge key). Both see the same URL.
- **Two tabs, different keys**: the second gets 409 with the first's URL.
- **Card added through `StartPaymentMethodSetupAsync` while a checkout is open**: the checkout still
  settles. Adoption skips, because a default exists. A new top-up on that subscription uses the stored card.
- **Subscription cancelled during checkout (`CancelAtPeriodEnd`)**: not "ended"; the purchase grants,
  matching a stored-card purchase that started before the cancellation. Grants are forfeited when it ends.
- **Plan change during checkout**: the grant uses the quote's expiry policy. The plan-change processor
  forfeits per spec 002's table. Not newly handled.
- **Provider removal in progress** (`payment_method_removal_in_progress`): the session is refused, purchase `Failed`.
- **Adyen sandbox**: the `Subscription` recurring model on Adyen is "not verified live" (remarks on
  `SubscriptionCheckoutService.ChargeAsync`). This feature needs that check (test plan, step L4).

## Non-Functional Requirements

- Stored-card purchases: no extra reads or writes beyond the one index-backed open-checkout query,
  and that query runs on the checkout path only.
- Webhook fast path: one point read by `(TenantId, PaymentId)` per transitioned payment id. Most
  payments are not credit purchases and return nothing.
- Logs and audit use `PaymentLogValue.Hash`/`Id`, as the rest of the credit service does. The
  checkout URL is never logged.

## Acceptance Criteria

- **AC-1** Given an Active subscription with no `DefaultPaymentMethodId` and a purchasable meter, when
  previewing, then 200 with `paymentPath = Checkout` (no `no_payment_method` refusal).
- **AC-2** Given the same, when purchasing 100 credits with key K, then 200, `status = AwaitingPayment`,
  a non-empty `checkoutUrl`, no grant, and one payment under `CreditPurchaseChargeKeyFor(purchaseId)`
  with `ShouldSavePaymentMethod = true` and `RecurringModel = Subscription`.
- **AC-3** Given AC-2, when K is resubmitted with the same payload, then the same `checkoutUrl` is
  returned and no second payment or provider session exists.
- **AC-4** Given AC-2, when K is resubmitted with a different quantity, then 409 `subscription_credits_idempotency_conflict`.
- **AC-5** Given AC-2, when a purchase with key K2 is submitted, then 409
  `subscription_credits_checkout_in_progress` carrying the first `checkoutUrl`.
- **AC-6** Given AC-2, when the browser return carries a success result but no webhook has arrived
  and the provider read is undecided, then no grant exists and the status is `AwaitingPayment`.
- **AC-7** Given AC-2, when the signed webhook confirms the payment, then on the same worker tick the
  purchase is `Finalized`, a `Purchased` grant of 100 exists, an invoice was announced with the checkout's
  payment id, and `SubscriptionCreditsPurchased` was published.
- **AC-8** Given AC-7, then the billing account's `DefaultPaymentMethodId` is the stored card saved by
  that checkout, and a following purchase charges it synchronously (`PaymentPath = StoredCard`, `Finalized` in-request).
- **AC-9** Given AC-7 on **Adyen**, then the card is adopted even though it has no `ProviderPayerReference`,
  and `hasPaymentMethod` reads true.
- **AC-10** Given a card was added to the account while the checkout was open, when the checkout
  settles, then credits are granted and `DefaultPaymentMethodId` is unchanged.
- **AC-11** (Stripe) Given AC-2 with the session completed but the webhook lost, when K is resubmitted,
  then the reconciler read confirms it and the response returns the finalized purchase and grant.
- **AC-12** Given AC-2 with a one-use discount, when the session expires unpaid and the sweep runs past
  `CheckoutExpiresAtUtc + grace`, then the purchase is `Failed` with `subscription_credits_checkout_expired`,
  the reservation is released, and a new purchase can use the code.
- **AC-13** Given AC-2, when the provider declines, then `Failed`, discount released, no grant.
- **AC-14** Given AC-2, when the subscription is cancelled immediately and the payment is then
  confirmed, then no grant exists and the purchase is `NeedsAttention`.
- **AC-15** Given a Reset meter quoted with `ExpiresAtUtc = T`, when payment is confirmed after T,
  then no grant exists and the purchase is `NeedsAttention`. Confirmed before T: grant with `ExpiresAtUtc = T`.
- **AC-16** Given a purchase already `Failed` for expiry, when a late confirmation arrives, then `NeedsAttention`, no grant.
- **AC-17** Given a confirmed amount that differs from `TotalMinor`, then `NeedsAttention`, no grant.
- **AC-18** Given no card and a quote total of 50 minor units, then 400 `subscription_credits_below_minimum_charge` and no session.
- **AC-19** Given no card and a 100% credit code (total 0), then 409 `subscription_credits_no_payment_method`.
- **AC-20** Given a `Pending` checkout purchase whose process died before the session opened, when the
  sweep runs, then it is `Failed` (no payment) or moved to `AwaitingPayment` (payment found).
- **AC-21** Given a stored-card subscription, then purchase behavior and every spec 002 AC are unchanged.
- **AC-22** Given the session lifetime, then Stripe sessions carry `expires_at` 60 minutes out and
  Adyen sessions expire in 60 minutes; other `MakePaymentAsync` callers send no expiry.
- **AC-23** Given a signup checkout and a credit checkout on the same subscription, then
  `FindBySubscriptionAsync`, `StartPaymentMethodSetupAsync` and activation recovery see only the
  signup link (no `SubscriptionPaymentLink` is written for credits).
- **AC-24** Given the simulation harness and a subscription with no card, when Buy credits is
  confirmed, then the dialog shows "Pay at checkout", opens `checkoutUrl` in a new tab, shows
  `AwaitingPayment`, and a "Check payment" action re-submits the same key until it is `Finalized` or `Failed`.
  The idempotency key is kept until then.

## Test Plan

### Unit (`server/XUnitTest`, existing credit-service test fixtures)

| Area | Cases |
|---|---|
| `PriceAsync` | no card → checkout path (AC-1); $0 with no card still refused (AC-19); floor before checkout (AC-18); existing refusals unchanged |
| `PurchaseAsync` checkout branch | request shape to `MakePaymentAsync`: key, save flag, recurring model, provider pinning (AC-2); session failure → `Failed` + release; second key → 409 (AC-5) |
| Replay | same URL, no second call (AC-3); fingerprint conflict (AC-4); Stripe read on replay (AC-11) |
| `SettleCheckoutAsync` | confirmed → `FinalizeAsync` (AC-7); declined (AC-13); ended (AC-14); Reset expiry passed (AC-15); late confirm after `Failed` (AC-16); amount mismatch (AC-17); a repeated call is idempotent (grant id from the purchase) |
| Card adoption | adopts when none (AC-8); skips when a default exists (AC-10); Adyen card without payer ref (AC-9); no card yet → `CardAdoptedAtUtc` null, retried |
| Sweep | every row of the sweep table, including AC-12 and AC-20; stored-card `Pending`/`Charged` paths unchanged |
| Activation processor | regression: signup adoption behavior unchanged after extraction |
| Factories | Stripe `expires_at` only when `CheckoutExpiresInMinutes` is set; Adyen `expiresAt`; existing callers unchanged (AC-22) |

### Integration

- Webhook fast path: `PaymentWorkCommandConsumer` with a transitioned payment id that belongs to a
  credit purchase finalizes it on the same tick (AC-7). An unrelated payment id is a no-op.
- Mongo index presence for `(TenantId, SubscriptionId, Status)` and `(TenantId, PaymentId)`.

### Client (`credits-card.test.tsx`)

- No-card subscription: the dialog shows the checkout hint from `paymentPath`; purchase opens the URL; status
  polling via the same key; the key is regenerated only after `Failed` (AC-24).

### Live on dev (manual; extend `e2e/manual/credit-simulation.console.js`)

- **L1 Stripe**: hidden $0 plan (allowance 0, overage disallowed, `Never` credits). Subscribe → buy
  100 → pay with the 4242 test card → `Finalized`, grant 100, invoice, `hasPaymentMethod = true` →
  buy 100 again → synchronous charge on the saved card.
- **L2 Stripe expiry**: open checkout with a one-use code, let it expire (or expire it via the
  Stripe API), run the sweep → `Failed`, code reusable.
- **L3 Stripe lost webhook**: pay with webhook delivery paused → re-POST the key → `Finalized` via reconciler.
- **L4 Adyen**: L1 on an Adyen-pinned tenant. Verify the token is stored without a consent checkbox,
  the card is adopted with no payer reference, and the second top-up's `Subscription`-model charge
  succeeds. This is also the first live check of the `Subscription` recurring model on Adyen.
- **L5 Usage**: after L1, record usage on the meter. Credits are consumed, and recording past the bought
  credits is refused (overage disallowed).
- Re-run the 31 existing credit checks to confirm no regression.

## Deferred Decisions

| Decision | Fallback chosen now | Revisit when |
|---|---|---|
| Force checkout with a card saved (decision 8) | not offered | a client wants to pay with a different card per top-up |
| Auto-refund for paid-but-ungrantable purchases | `NeedsAttention` + manual refund | the first such record in production |
| Late confirmation after expiry (decision 14) | `NeedsAttention` | it happens more than rarely on Adyen |
| $0 purchase with no card | refused | the wallet client asks for free-code top-ups without a card |
| Expiry grace | 15 minutes, `SubscriptionOptions` | first false `Failed` from a slow Adyen webhook |

## Open Questions

(none. The guard in decision 6 and decisions 14 and 15 are the author's call, flagged for review.)
