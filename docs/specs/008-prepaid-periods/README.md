# Prepaid periods

Issue: SELISEdigitalplatforms/blocks-utilities#808 ([AmLora] Purchase plan for N number of periods at a time).

## Intent

Let a buyer pay for several periods of a recurring plan in one payment, for example five years of a yearly plan or six months of a monthly plan, instead of being charged every period.

## Decisions (2026-11-24)

1. **Price.** N × the period price. The price's automatic discount, volume band, tax and a promotional code apply exactly as they do to N separate period charges. No new pricing model.
2. **After the term.** The subscription keeps running and is charged per period as usual, unless it was cancelled.
3. **Choosing N.** The buyer sends `prepaidPeriods` when subscribing (default 1). Each price carries an optional `maxPrepaidPeriods`; a price without one cannot be prepaid.
4. **Mid-term changes.** Same rules the prepaid calendar year already follows:
   - An upgrade or more seats applies now and charges the difference for what is left of the current period **plus every prepaid period still to come**.
   - A downgrade, fewer seats, a cadence change, an extended price edit or a cancellation takes effect when the prepaid term ends. Nothing is refunded.
   - "Cancel now" during a prepaid term is honoured as cancel at term end (the buyer keeps what they paid for).
5. **Calendar-aligned prices (billing on the 1st, which AmLora uses).** The partial period up to the 1st comes on top: a signup on 14 March 2026 prepaying 5 years pays 14–31 March plus 5 whole years, 1 April 2026 – 1 April 2031. Everything is collected at checkout, whatever the price's annual charge timing says. Decided 2026-11-24.

## Non-goals

- Prepaying a signup that gets a **trial**. Nothing is charged at signup during a trial, so there is nothing to prepay with. Refused with `subscription_prepaid_periods_unavailable`.
- Buying more prepaid periods in the middle of a running subscription (top-up / extension).
- Refunds of unused prepaid periods.

## Design

### Catalogue

- `Price.MaxPrepaidPeriods` (`int?`). Null or 1: not prepayable. Authoring range 1–120 (a safety cap, `SubscriptionConstants.MaxPrepaidPeriods`).
- `CreatePriceRequest.MaxPrepaidPeriods`, `PriceResponse.MaxPrepaidPeriods`. Console plan builder gets a "Max prepaid periods" field.

### Signup

- `CreateSubscriptionRequest.PrepaidPeriods` (`int?`, default 1). Refused when above the price's max, below 1, or the signup has a trial.
- The opening charge is the ordinary opening charge plus the whole periods it does not already cover (`SubscriptionAmountCalculator.PrepaidExtraCharge`):
  - anniversary price: period 1 + N−1 more;
  - calendar monthly with a stub: stub + N months;
  - calendar yearly with a stub: stub + year 1 (the pending annual period, forced to `CollectedWithCheckout`) + N−1 more years;
  - calendar signup on the 1st: no stub, same as anniversary.
  A promotional code reduces at most `DurationPeriods` periods across the term. All parts (gross, discounts, net, tax, total) are summed, so the invoice and the payment record still add up.
- New fields on `SubscriptionDetail`:
  - `PrepaidPeriods`: N as bought (0 on older records = 1).
  - `PrepaidThroughUtc`: end of the last prepaid period, computed from the fee schedule at signup.
  - `PrepaidDiscountPeriods`: how many of the N periods the promotional code reduced.
- Activation (card and offline) records `PrepaidDiscountPeriods` as `DiscountPeriodsApplied`, so a three-period code is spent by a prepaid term that used it three times.
- Resuming a pending checkout with a different `prepaidPeriods` is a conflict, like a different plan or quantity.

### Renewal

- The fee schedule still turns over every period (usage, period keys and events stay per period).
- A renewal opening a period that starts before `PrepaidThroughUtc` charges 0: no gateway call, no invoice, no card required.
- The first renewal at or after `PrepaidThroughUtc` is an ordinary charge.

### Paid-through date

`SubscriptionPaidPeriod.PaidThroughUtc` returns `PrepaidThroughUtc` while it is later than the current period's end. Everything that already waits for "the end of what was paid" (scheduled downgrade, quantity decrease, extended price terms) therefore waits for the term end with no further change.

### Plan and quantity changes

- `PlanChangeClassifier` treats a running prepaid term like a paid annual term: a cadence or alignment change always waits for the term end.
- An immediate same-cadence plan change during a prepaid term keeps the current period and fee schedule (does not restart the cycle today), so `PrepaidThroughUtc` stays valid.
- `SubscriptionProrationCalculator.Calculate` adds, to both sides of the settlement, the whole-period value (never a stub fraction) × the number of prepaid periods still to come after the current one. The breakdown, the classification (upgrade vs downgrade), the charge and the settlement invoice all see the term-wide figure. With the cycle kept, the target is priced as the current period (`targetIsCurrentPeriod`).
- During a calendar yearly stub, `CalculateOpeningStubUpgrade` adds the same difference for the years after year 1 to the combined settlement.

### Cancellation

During a prepaid term: `CanCancelImmediately = false`, and the cancellation takes effect at `PrepaidThroughUtc` (written as the current period's end, as the prepaid year does).

### Invoice and response

- The opening invoice states the whole term as its period: a stub line over its own dates when there is one, then one "N × <interval>" line over the term's dates, with the unit price for the term.
- `SubscriptionResponse` gains `prepaidPeriods` and `prepaidThroughUtc`; `nextPaymentAtUtc` is the term end while the term runs.
- The purchase preview's due-now figure and breakdown are the N-period charge; its next charge is dated at the term end.

## Tests

- Opening charge for N periods, with tax, built-in discount, and a promotional code shorter than N.
- Calendar monthly (stub + N months) and calendar yearly (stub + N years, year 1 collected at checkout).
- Validation: above max, trial, no max.
- Renewal inside the term charges nothing and needs no card; renewal at the term end charges.
- Cancellation inside the term is scheduled for the term end and cannot be escalated.
- Seat increase inside the term charges current remainder + remaining periods; decrease is scheduled for the term end.
- Paid-through date and next payment date.
