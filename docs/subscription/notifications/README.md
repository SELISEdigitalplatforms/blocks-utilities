# Subscription notification emails

Written for the person **setting up a tenant's email templates**. It covers which emails the
subscription module sends, who receives them, and the placeholders each one can use.

The subscription module never writes email text. It publishes a mail request with a **purpose**,
a **language** and a set of **values**. The platform mail module then picks your template by
purpose and language and fills in the values. If you don't create a template, no email is sent.

## The seven emails

| Purpose | Sent when | To |
| --- | --- | --- |
| `subscription_quantity_changed` | A quantity actually changes: an increase that has been paid for, or a scheduled decrease carried out at renewal | Billing contact |
| `subscription_plan_changed` | A plan change actually takes effect: immediately, or at the renewal it was scheduled for | Billing contact |
| `subscription_cancellation_requested` | Someone cancels for the end of the paid period | Billing contact |
| `subscription_canceled` | A subscription ends: an immediate cancel, or a scheduled one reaching its date | Billing contact |
| `subscription_cancellation_withdrawn` | A scheduled cancellation is undone | Billing contact |
| `subscription_member_assigned` | An administrator gives someone a seat on a per-person subscription | That person |
| `subscription_member_removed` | An administrator takes a seat back | That person |

Nothing is sent when a decrease or plan change is merely *scheduled*. The email goes out when it
happens. When a cancellation takes effect, every seat is released, but the members get no
`subscription_member_removed`; the billing contact's `subscription_canceled` is the notice.

## Placeholders

Every key below is **always sent**, as an empty string when it does not apply. Write `{{Key}}` in
the template.

**Billing-contact emails** (the first five):

| Key | Value |
| --- | --- |
| `DisplayName` | The billing contact's name, else their address |
| `PlanName`, `PlanCode` | The plan the subscription is on after the change |
| `PreviousPlanName` | Plan changes: the plan that was left |
| `QuantityChanges` | Quantity changes: one entry per item that moved, e.g. `Seats: 5 → 10; Projects: 2 → 3` |
| `EffectiveDate` | `yyyy-MM-dd`, in the subscription's own timezone. For a cancellation, the day access ends; for a change, the day it took effect; empty for a withdrawal |
| `ActorName` | Who asked for it. For a change carried out later at renewal, the person who scheduled it |
| `CancellationReason` | Cancellations: the reason the canceller typed. **Body only**; it is never available in the subject |

**Member emails** (the last two):

| Key | Value |
| --- | --- |
| `DisplayName` | The member's name from IAM, else their address |
| `PlanName`, `PlanCode` | The plan of the subscription the seat is on |
| `OrganizationName` | The organization, as IAM names it |
| `ActorName` | The administrator who gave or took back the seat |

You don't need to escape anything. The mail module HTML-encodes body values itself.

## Languages, and why an email can go missing

The mail module only uses a template whose language matches the request **exactly**, and it has
**no fallback**. A request for `de-CH` will not use your `en-US` template; it is dropped silently.

- **Billing contacts** get the billing profile's *Email language*, else the language given when
  the subscription was created, else `en-US`.
- **Members** get their language from IAM, else `en-US`.

So create every purpose in every language your users and billing contacts have set. Also note two
more cases where no email arrives, both decided by the mail module:

- a template that uses a `{{Key}}` not listed above;
- a recipient address that is not a registered user of the tenant. A billing contact who is an
  outside accounts-payable mailbox does not receive these emails.

## Checking what happened

Each email the subscription module hands over, or decides not to send, is recorded as a mail
delivery report with source `SubscriptionNotification`. Skipped emails carry a reason:
`billing_email_missing`, `member_email_missing`, `subscription_not_found`, or
`seat_change_not_applied`. The last one means the email described a seat change that never
landed, so it was deliberately not sent. "Published" means the mail queue accepted it, not that
it was delivered; delivery is the mail module's record. See [tracing/](../tracing/) for reading
these.
