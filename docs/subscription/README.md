# Subscription docs

Written for the person **authoring plans** in the plan builder, not for the person maintaining the
billing engine. If you are picking between two dropdown options and cannot tell what either one
means, start here.

| Document | Read it when |
| --- | --- |
| [plan-authoring/](plan-authoring/) | You are building a plan and need to know what every option means, when to pick each alternative, and how to prove you picked right. |
| [lifecycle/](lifecycle/) | You need the whole journey — signup, trial, renewal, upgrade, overage, decline, cancellation — and what the subscriber sees at each step. |
| [per-person/](per-person/) | A plan is sold to **each person** rather than the organization: places, members, per-place allowances, every endpoint with its payload, and the permissions to grant. |
| [v1/emails/](v1/emails/) | You need every email the module sends — invoices, credit notes, notices, usage warnings — with its trigger, recipient, language, placeholders and the exact payload put on the mail queue. |
| [notifications/](notifications/) | You are writing the tenant's email templates for seat, plan, cancellation and member changes, and need each purpose, its recipient and its placeholders. |
| [tracing/](tracing/) | A customer has reported a problem and you need to find out what actually happened to their subscription. |

For how any of it is *implemented*, the authority is
[`server/Subscription.DomainService/README.md`](../../server/Subscription.DomainService/README.md).
Where these two disagree, that one is right and this one is a bug.

## The 60-second version

Six decisions make a plan. Everything else is a detail hanging off one of them.

| # | Decision | The question it answers |
| --- | --- | --- |
| 1 | **Quantity item** | How many did they *buy*? (seats, licences, branches) |
| 2 | **Meter** | How much did they *use*? (screenings, API calls, GB) |
| 3 | **Entitlement** | May they do it? — the runtime permission check |
| 4 | **Price** | What does it cost, how often, and does quantity multiply it? |
| 5 | **Trial** | Do they get in free first, and do we hold a card while they are? |
| 6 | **Payment method** | Do we require a card even when nothing is due today? |

Two sentences catch most authoring mistakes:

> **Nouns you hold are quantity. Verbs you perform are meters.**
> API keys are quantity; API calls are a meter.

> **"Allowed" and "paid for" are different questions.**
> A meter bills. An entitlement permits. Nothing forces them to agree, and the builder will warn
> you rather than stop you when they don't.

## The one rule the whole module is built on

From the server README, and worth knowing before you author anything:

> **The platform never learns a domain word.** There is no `Seats` column and no `ScreeningCount`.

Quantities carry a unit label *you* choose. Usage flows through meters *you* name. Plan features
are a JSON bag stored verbatim and never interpreted. One product sells seats and meters
screenings; another sells workspaces and meters envelopes. Both are configuration, not code.

So if you are waiting for someone to add "screening support" to the module — nobody will, and
nobody needs to. You author it.

## Credits without a plan

Since [spec 005](../specs/005-standalone-credit-purchase/README.md) an organization can buy a
meter's credits without subscribing to anything. Three pieces, all authored under the plan
permissions:

| Piece | What it holds | Scope |
| --- | --- | --- |
| **Meter catalogue** | A meter's identity: key, name, unit, aggregation, scale. No money. | Tenant-wide, with an optional per-organization override that wins for that organization. |
| **Credit offer** | One per meter per scope: price per currency, min/max purchase, tax, expiry (fixed time after purchase, or never) and the *spend order*. | Same resolution as plans: the organization's own offer, otherwise the tenant's. |
| **Organization credits** | What was bought or granted. Belongs to the organization, not a subscription. | Spent with or without a subscription; nothing a subscription does can forfeit them. |

Plans pick meters from the catalogue, and a meter you author inside a plan is added to the
catalogue on save, so it starts empty and fills itself. The plan still prices and expires credits
its own way (`topUp` terms); the offer is a second, independent price for the same meter.

**Spend order.** When an organization holds standalone credits *and* a subscription that meters
the same key, the offer's `spendOrder` decides: `SubscriptionFirst` uses the plan allowance, then
the subscription's own credits, then the organization's, then overage; `OrganizationCreditsFirst`
spends the organization's credits before the allowance is touched.

**No subscription at all.** Usage draws on the organization's credits alone, and is refused outright
when they run out — there is no allowance to fall back on and nothing to bill overage to. The
response says `subscription_credits_exhausted`; the fix is to buy more.
