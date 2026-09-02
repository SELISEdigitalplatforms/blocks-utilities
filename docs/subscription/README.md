# Subscription docs

Written for the person **authoring plans** in the plan builder, not for the person maintaining the
billing engine. If you are picking between two dropdown options and cannot tell what either one
means, start here.

| Document | Read it when |
| --- | --- |
| [plan-authoring/](plan-authoring/) | You are building a plan and need to know what every option means, when to pick each alternative, and how to prove you picked right. |
| [lifecycle/](lifecycle/) | You need the whole journey — signup, trial, renewal, upgrade, overage, decline, cancellation — and what the subscriber sees at each step. |

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
