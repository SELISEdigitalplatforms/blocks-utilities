# Subscription emails — the complete reference (v1)

Written for the person **creating or debugging a tenant's email templates**, and for the engineer
who needs to know exactly what the subscription module puts on the mail queue.

Every email the subscription module sends goes out the same way:

1. Something happens (a seat changes, an invoice is issued, usage crosses a threshold).
2. The module builds a **mail command** with a `Purpose`, a `Language`, a recipient and two
   dictionaries of placeholder values, and publishes it to the `blocks_email_listener` queue.
3. The platform mail module picks the tenant's template by **purpose and language**, fills the
   placeholders and sends.

The module never writes email text. **No template for the purpose and language means no email.**

| Family | Purposes | Sent by | Delivered through |
| --- | --- | --- | --- |
| [Financial documents](#financial-documents) | `subscription_invoice`, `subscription_trial_invoice`, `subscription_credit_note` | `SubscriptionFinancialDocumentDeliveryService` | Worker work queue, `FinancialDocumentDelivery` work type |
| [Billing-contact notices](#billing-contact-notices) | `subscription_quantity_changed`, `subscription_plan_changed`, `subscription_cancellation_requested`, `subscription_canceled`, `subscription_cancellation_withdrawn` | `SubscriptionNotificationEmailService` | Worker `SubscriptionLifecycleMailConsumer`, fed by the lifecycle topic |
| [Member notices](#member-notices) | `subscription_member_assigned`, `subscription_member_removed` | `SubscriptionNotificationEmailService` | same consumer |
| [Usage warning](#usage-warning) | `subscription_usage_threshold` | `UsageThresholdEmailService` | same consumer |
| [Card expiry](#card-expiry) | `subscription_payment_method_expiring` | `PaymentMethodExpiryEmailService` | Worker work queue, `PaymentMethodExpiryNotice` work type |

Twelve purposes in total. Nothing else in this repository sends email.

## The wire payload

Every email is one `SendMail` message. The shape is fixed by the Blocks OS mail listener, and the
subscription module fills only the fields below; everything else is sent empty.

```json
{
  "To": ["billing@example.com"],
  "Cc": [],
  "Bcc": [],
  "ReplyTo": [],
  "Purpose": "subscription_invoice",
  "Language": "en-US",
  "Attachments": ["<storage file id>"],
  "SubjectDataContext": { "Key": "value" },
  "BodyDataContext": { "Key": "value" },
  "CorrelationId": "<id of the thing that caused the mail>"
}
```

| Field | What the module puts there |
| --- | --- |
| `To` | Exactly one address, trimmed and lower-cased. Never more than one recipient. |
| `Cc`, `Bcc`, `ReplyTo` | Always empty. |
| `Purpose` | One of the twelve purposes above. Selects the template. |
| `Language` | BCP-47 tag (`en-US`, `de-CH`). Selects the template too; see [Languages](#languages). |
| `Attachments` | Storage file ids. Only financial documents attach anything (the PDF). |
| `SubjectDataContext` | Placeholders usable in the subject line. Values are placed **raw**. |
| `BodyDataContext` | Placeholders usable in the body. Values are **HTML-encoded** by the mail module. |
| `CorrelationId` | Echoed on every mail-module log line and outcome, so a mail can be matched back to the event, document or usage crossing behind it. |

Write a placeholder as `{{Key}}`. **A template that uses a key the payload does not carry is
rejected by the mail module after the message has been acknowledged, silently.** That is why every
purpose in a family always sends the family's full key set, with an empty string where a value does
not apply.

Each family section below ends with a **starter template** per purpose: a subject line and an HTML
body that use only the keys that purpose sends. They are a safe starting point for a tenant's
templates, not the wording the product ships with.

## Financial documents

A PDF is rendered, stored, and attached; the email is the covering note. The module is
**at-most-once** here on purpose: two identical invoice emails read as being billed twice. A
document whose mail outcome is unknown is left for an operator to resend deliberately.

| Purpose | Document | Issued when |
| --- | --- | --- |
| `subscription_invoice` | Invoice | A charge settled: first checkout, renewal, paid quantity increase, paid plan change, usage charge. Also an opening period that activated with nothing due (a zero or fully discounted price). |
| `subscription_trial_invoice` | Trial invoice, zero total | A trial started. Documents the terms of the free period. |
| `subscription_credit_note` | Credit note, linked to the invoice it adjusts | A refund confirmed on the payment side, or a downgrade whose unused time was banked as credit. Credit later *spent* on an invoice gets no credit note; it appears as a deduction on that invoice. |

**Trigger path.** The money path records the obligation on the subscription and schedules
`FinancialDocumentDelivery` work. Four sweeps backstop that record: recorded obligations, settled
charges with no document, confirmed refunds, and trials with no trial invoice. So a document can
arrive minutes later than the event if the worker was down, but it does arrive. Resends go through
`POST /subscriptions/invoices/{documentId}/resend` and travel the same work type.

**Recipient.** The billing contact frozen on the document (`BillingContact.Email`). No address
means the document stays downloadable and the mail is recorded as not attempted.

**Language.** Always `en-US`. The billing profile's language is not consulted on this path.

**Placeholders.** Identical for all three purposes, in both subject and body:

| Key | Value |
| --- | --- |
| `MessageId` | The mail's own id, `document-mail:<documentId>`. Stable across resends of the same document. |
| `DocumentNumber` | The allocated number, prefix `INV` or `CRN`. |
| `DocumentType` | `Invoice`, `TrialInvoice` or `CreditNote`. |
| `OrganizationName` | The subscriber's legal name as frozen on the document. |
| `ContactName` | The billing contact's name as frozen on the document. May be empty. |
| `PlanName` | The plan the document is for. |
| `Total` | Formatted: currency code, space, amount with the currency's decimal places and grouping (`CHF 1'200.00`, `EUR 1,200.00`). Negative for a credit note: `-CHF 50.00`. Zero on a trial invoice: `CHF 0.00`. |
| `CurrencyCode` | Upper-case ISO code. |
| `IssuedOn` | `yyyy-MM-dd`, UTC. |
| `PeriodStart`, `PeriodEnd` | `yyyy-MM-dd` in the subscription's billing timezone. Empty on a document that belongs to no period, such as a settlement credit note. |

**Full payload example** (`subscription_invoice`; the other two differ only in `Purpose`,
`DocumentType`, `DocumentNumber` and `Total`):

```json
{
  "To": ["accounts@acme.example"],
  "Cc": [], "Bcc": [], "ReplyTo": [],
  "Purpose": "subscription_invoice",
  "Language": "en-US",
  "Attachments": ["7f3c9d2e-storage-file-id"],
  "SubjectDataContext": {
    "MessageId": "document-mail:6650c1f2a9b3d4e5f6a7b8c9",
    "DocumentNumber": "INV-2026-000123",
    "DocumentType": "Invoice",
    "OrganizationName": "Acme GmbH",
    "ContactName": "Dana Keller",
    "PlanName": "Team",
    "Total": "CHF 1'200.00",
    "CurrencyCode": "CHF",
    "IssuedOn": "2026-10-01",
    "PeriodStart": "2026-10-01",
    "PeriodEnd": "2026-10-31"
  },
  "BodyDataContext": {
    "MessageId": "document-mail:6650c1f2a9b3d4e5f6a7b8c9",
    "DocumentNumber": "INV-2026-000123",
    "DocumentType": "Invoice",
    "OrganizationName": "Acme GmbH",
    "ContactName": "Dana Keller",
    "PlanName": "Team",
    "Total": "CHF 1'200.00",
    "CurrencyCode": "CHF",
    "IssuedOn": "2026-10-01",
    "PeriodStart": "2026-10-01",
    "PeriodEnd": "2026-10-31"
  },
  "CorrelationId": "document-mail:6650c1f2a9b3d4e5f6a7b8c9"
}
```

`CorrelationId` on this family is the `MessageId`, not the checkout's correlation id: the whole
checkout shares one correlation id, and this one names exactly one mail.

**Starter templates.** One body serves all three; only the subject and the opening line differ.
`{{Total}}` is already formatted with its currency, and `{{PeriodStart}}`/`{{PeriodEnd}}` are empty on a
document that belongs to no period, so the period line is worded to read well either way.

`subscription_invoice`

```
Subject: Invoice {{DocumentNumber}} for {{PlanName}}: {{Total}}
```

```html
<p>Hello {{ContactName}},</p>
<p>Your invoice <strong>{{DocumentNumber}}</strong> for <strong>{{OrganizationName}}</strong> is attached.</p>
<table>
  <tr><td>Plan</td><td>{{PlanName}}</td></tr>
  <tr><td>Period</td><td>{{PeriodStart}} to {{PeriodEnd}}</td></tr>
  <tr><td>Issued</td><td>{{IssuedOn}}</td></tr>
  <tr><td>Total</td><td><strong>{{Total}}</strong></td></tr>
</table>
<p>The amount has been charged to the card on file. No action is needed.</p>
<p style="color:#888;font-size:12px">Reference {{MessageId}}</p>
```

`subscription_trial_invoice`

```
Subject: Your trial of {{PlanName}} has started
```

```html
<p>Hello {{ContactName}},</p>
<p>Your free trial of <strong>{{PlanName}}</strong> for <strong>{{OrganizationName}}</strong> runs from
{{PeriodStart}} to {{PeriodEnd}}. The attached document <strong>{{DocumentNumber}}</strong> records the terms of
the trial; its total is {{Total}} and nothing has been charged.</p>
<p>Issued {{IssuedOn}}.</p>
<p style="color:#888;font-size:12px">Reference {{MessageId}}</p>
```

`subscription_credit_note`

```
Subject: Credit note {{DocumentNumber}}: {{Total}}
```

```html
<p>Hello {{ContactName}},</p>
<p>A credit note <strong>{{DocumentNumber}}</strong> has been issued to <strong>{{OrganizationName}}</strong>
for <strong>{{Total}}</strong> against your {{PlanName}} subscription. The document is attached.</p>
<p>Issued {{IssuedOn}}.</p>
<p style="color:#888;font-size:12px">Reference {{MessageId}}</p>
```

## Billing-contact notices

Five emails to the billing contact of the subscription, one per kind of change. Each is converted
from a lifecycle event the worker reads off `blocks_subscription_lifecycle_topic`.

| Purpose | Lifecycle event | Sent when |
| --- | --- | --- |
| `subscription_quantity_changed` | `SubscriptionQuantityChanged` | A quantity **actually** changes: a free increase applied at once, a paid increase once its charge or checkout settles, or a scheduled decrease carried out at renewal. |
| `subscription_plan_changed` | `SubscriptionPlanChanged` | A plan change **takes effect**: immediately (free, stored-card charge, or hosted checkout settled), or at the renewal it was scheduled for. |
| `subscription_cancellation_requested` | `SubscriptionCancellationRequested` | Someone cancels for the end of the paid period. |
| `subscription_canceled` | `SubscriptionCanceled` | The subscription ends: an immediate cancel, or a scheduled one reaching its date (the cancellation-effective processor). |
| `subscription_cancellation_withdrawn` | `SubscriptionCancellationWithdrawn` | A scheduled cancellation is undone. |

Nothing is sent when a decrease or plan change is merely *scheduled*; the email goes out when it
happens. When a cancellation takes effect every seat is released, but members get no
`subscription_member_removed`; this family's `subscription_canceled` is the notice.

**Recipient.** The billing account's `BillingEmail`. The account is read at send time, so an
address changed after the event still receives it.

**Language.** First non-empty of: the billing profile's *Email language*, the language given when
the subscription was created, `en-US`. Read at send time, so a change reaches the very next email.

**Placeholders.** Every purpose sends all eight keys in the body; the subject gets the same set
**minus `CancellationReason`**, because subject values are placed raw and the reason is free text
typed by the canceller.

| Key | Value | Filled for |
| --- | --- | --- |
| `DisplayName` | The billing contact's name, else their address | all |
| `PlanName` | The plan the subscription is on after the change | all |
| `PlanCode` | Its code | all |
| `PreviousPlanName` | The plan that was left | plan changed |
| `QuantityChanges` | One entry per item that moved, `UnitLabel: old → new`, joined with `; ` (`Seats: 5 → 10; Projects: 2 → 3`). The item key stands in when a unit label is blank. | quantity changed |
| `EffectiveDate` | `yyyy-MM-dd` in the subscription's billing timezone. The day access ends for a cancellation request or a cancel; the day the change took effect for a quantity or plan change; **empty** for a withdrawal. | all but withdrawn |
| `ActorName` | Who asked for it. For a change carried out at renewal, the person who scheduled it. | all; empty when system-initiated |
| `CancellationReason` | The reason the canceller typed. **Body only.** | cancellation requested, canceled |

**Full payload example** (`subscription_plan_changed`):

```json
{
  "To": ["accounts@acme.example"],
  "Cc": [], "Bcc": [], "ReplyTo": [], "Attachments": [],
  "Purpose": "subscription_plan_changed",
  "Language": "de-CH",
  "SubjectDataContext": {
    "DisplayName": "Dana Keller",
    "PlanName": "Business",
    "PlanCode": "business-monthly",
    "PreviousPlanName": "Team",
    "ActorName": "Sam Rivera",
    "QuantityChanges": "",
    "EffectiveDate": "2026-10-15"
  },
  "BodyDataContext": {
    "DisplayName": "Dana Keller",
    "PlanName": "Business",
    "PlanCode": "business-monthly",
    "PreviousPlanName": "Team",
    "ActorName": "Sam Rivera",
    "QuantityChanges": "",
    "EffectiveDate": "2026-10-15",
    "CancellationReason": ""
  },
  "CorrelationId": "<lifecycle EventId>"
}
```

What changes per purpose, everything else staying as above:

| Purpose | `PreviousPlanName` | `QuantityChanges` | `EffectiveDate` | `CancellationReason` |
| --- | --- | --- | --- | --- |
| `subscription_quantity_changed` | `""` | `Seats: 5 → 10` | date applied | `""` |
| `subscription_plan_changed` | old plan | `""` | date applied | `""` |
| `subscription_cancellation_requested` | `""` | `""` | last day of access | typed reason |
| `subscription_canceled` | `""` | `""` | last day of access | typed reason |
| `subscription_cancellation_withdrawn` | `""` | `""` | `""` | `""` |

**Starter templates.** `{{CancellationReason}}` is body-only; a subject that names it is rejected.
`{{ActorName}}` is empty for a change the system made, so it is kept out of the subject.

`subscription_quantity_changed`

```
Subject: Your {{PlanName}} subscription has changed
```

```html
<p>Hello {{DisplayName}},</p>
<p>The quantities on your <strong>{{PlanName}}</strong> subscription changed on {{EffectiveDate}}:</p>
<p><strong>{{QuantityChanges}}</strong></p>
<p>Requested by {{ActorName}}.</p>
```

`subscription_plan_changed`

```
Subject: You are now on {{PlanName}}
```

```html
<p>Hello {{DisplayName}},</p>
<p>Your subscription moved from <strong>{{PreviousPlanName}}</strong> to <strong>{{PlanName}}</strong>
on {{EffectiveDate}}.</p>
<p>Requested by {{ActorName}}.</p>
```

`subscription_cancellation_requested`

```
Subject: Your {{PlanName}} subscription ends on {{EffectiveDate}}
```

```html
<p>Hello {{DisplayName}},</p>
<p>Your <strong>{{PlanName}}</strong> subscription has been set to end on <strong>{{EffectiveDate}}</strong>.
Access continues until then.</p>
<p>Reason given: {{CancellationReason}}</p>
<p>Requested by {{ActorName}}. Changed your mind? Withdraw the cancellation from your subscription page
before that date.</p>
```

`subscription_canceled`

```
Subject: Your {{PlanName}} subscription has ended
```

```html
<p>Hello {{DisplayName}},</p>
<p>Your <strong>{{PlanName}}</strong> subscription ended on <strong>{{EffectiveDate}}</strong>.</p>
<p>Reason given: {{CancellationReason}}</p>
<p>Requested by {{ActorName}}. You can subscribe again at any time.</p>
```

`subscription_cancellation_withdrawn`

```
Subject: Your {{PlanName}} subscription continues
```

```html
<p>Hello {{DisplayName}},</p>
<p>The scheduled cancellation of your <strong>{{PlanName}}</strong> subscription has been withdrawn.
It continues and renews as before.</p>
<p>Requested by {{ActorName}}.</p>
```

## Member notices

Two emails to the **person**, not the billing contact, on a per-person subscription.

| Purpose | Lifecycle event | Sent when |
| --- | --- | --- |
| `subscription_member_assigned` | `SubscriptionMemberAssigned` | An administrator gives someone a seat. |
| `subscription_member_removed` | `SubscriptionMemberReleased` | An administrator takes a seat back. |

**Before sending**, the service re-reads the seat the event names. The event is written before the
seat, so it may describe a seat that never landed (a crash between the two writes, or a person who
was already seated). Assigned sends only if the seat exists and is still held; removed sends only
if it exists and is released. Otherwise the mail is recorded as not attempted with
`seat_change_not_applied`.

**Recipient.** The member's email as IAM reported it when the event was written. No address means
`member_email_missing`.

**Language.** The member's IAM language, else `en-US`.

**Placeholders.** Same five keys in subject and body:

| Key | Value |
| --- | --- |
| `DisplayName` | The member's IAM display name, else their address |
| `PlanName`, `PlanCode` | The plan of the subscription the seat is on |
| `OrganizationName` | The organization, as IAM names it |
| `ActorName` | The administrator who gave or took back the seat |

**Full payload example** (`subscription_member_assigned`):

```json
{
  "To": ["jo.park@acme.example"],
  "Cc": [], "Bcc": [], "ReplyTo": [], "Attachments": [],
  "Purpose": "subscription_member_assigned",
  "Language": "en-US",
  "SubjectDataContext": {
    "DisplayName": "Jo Park",
    "PlanName": "Team",
    "PlanCode": "team-monthly",
    "OrganizationName": "Acme GmbH",
    "ActorName": "Dana Keller"
  },
  "BodyDataContext": {
    "DisplayName": "Jo Park",
    "PlanName": "Team",
    "PlanCode": "team-monthly",
    "OrganizationName": "Acme GmbH",
    "ActorName": "Dana Keller"
  },
  "CorrelationId": "<lifecycle EventId>"
}
```

`subscription_member_removed` is identical apart from `Purpose`.

**Starter templates.**

`subscription_member_assigned`

```
Subject: You now have a seat on {{PlanName}} at {{OrganizationName}}
```

```html
<p>Hello {{DisplayName}},</p>
<p>{{ActorName}} gave you a seat on the <strong>{{PlanName}}</strong> subscription of
<strong>{{OrganizationName}}</strong>. Sign in to start using it.</p>
```

`subscription_member_removed`

```
Subject: Your seat on {{PlanName}} at {{OrganizationName}} was removed
```

```html
<p>Hello {{DisplayName}},</p>
<p>{{ActorName}} removed your seat on the <strong>{{PlanName}}</strong> subscription of
<strong>{{OrganizationName}}</strong>. Contact them if you think this is a mistake.</p>
```

## Usage warning

One email, `subscription_usage_threshold`, from the `UsageThresholdReached` lifecycle event.

**Sent when** a metered counter crosses one of the meter's configured `ThresholdPercents`
(set per meter in the plan). The check is edge-triggered on the increment that crosses the line,
and the database decides which concurrent process gets to raise it, so each threshold fires
**once per meter per billing period**. Crossing 80% in October is a new event even if 80% was
already announced in September.

**Recipient.** The billing account's `BillingEmail`, read at send time.

**Language.** Always `en-US`. The billing profile's language is not consulted on this path.

**Placeholders.** Same seven keys in subject and body:

| Key | Value |
| --- | --- |
| `DisplayName` | The billing contact's name, else their address |
| `PlanName`, `PlanCode` | The subscription's current plan |
| `MeterKey` | The meter that crossed, as the plan names it |
| `ThresholdPercent` | The threshold crossed, as a whole number (`80`) |
| `Balance` | Consumption at the crossing, trailing zeroes trimmed (`800`, not `800.000000`) |
| `Limit` | The meter's limit for the period, formatted the same way |

**Full payload example**:

```json
{
  "To": ["accounts@acme.example"],
  "Cc": [], "Bcc": [], "ReplyTo": [], "Attachments": [],
  "Purpose": "subscription_usage_threshold",
  "Language": "en-US",
  "SubjectDataContext": {
    "DisplayName": "Dana Keller",
    "PlanName": "Team",
    "PlanCode": "team-monthly",
    "MeterKey": "screenings",
    "ThresholdPercent": "80",
    "Balance": "800",
    "Limit": "1000"
  },
  "BodyDataContext": {
    "DisplayName": "Dana Keller",
    "PlanName": "Team",
    "PlanCode": "team-monthly",
    "MeterKey": "screenings",
    "ThresholdPercent": "80",
    "Balance": "800",
    "Limit": "1000"
  },
  "CorrelationId": "<lifecycle EventId>"
}
```

**Starter template.**

`subscription_usage_threshold`

```
Subject: {{ThresholdPercent}}% of your {{MeterKey}} allowance used
```

```html
<p>Hello {{DisplayName}},</p>
<p>Your <strong>{{PlanName}}</strong> subscription has used <strong>{{Balance}}</strong> of
<strong>{{Limit}}</strong> {{MeterKey}} this period, which is past the {{ThresholdPercent}}% mark.</p>
<p>Usage beyond the limit is charged at the plan's overage rate. Consider upgrading if this happens
every period.</p>
```

## Card expiry

One email, `subscription_payment_method_expiring`, from the repair sweep rather than from an
event: there is no event for a date approaching.

**Sent when** an active stored card's expiry month ends within `PaymentMethodExpiryNoticeDays`
(default 30) of now, measured to the last day of that month, **and** a billing account charges
that card as its default. A card no account charges is marked as noticed without a mail, so it
stops coming back. Each card is told **once per expiry**: the month it was announced for is
recorded on the card, and a card re-saved with a later expiry is simply a new match.

**Recipient.** The `BillingEmail` of the billing account whose default the card is.

**Language.** Billing profile's *Email language*, else the billing account's language, else
`en-US`. Same rule as the billing-contact notices.

**Placeholders.** Same six keys in subject and body:

| Key | Value |
| --- | --- |
| `DisplayName` | The billing contact's name, else their address |
| `Brand`, `LastFour` | The card as the provider reported it (`visa`, `4242`) |
| `ExpiryMonth`, `ExpiryYear` | Zero-padded (`03`, `2030`), whatever spelling the provider stored |
| `ExpiryDate` | The last day the card works, `yyyy-MM-dd` |

**Full payload example**:

```json
{
  "To": ["accounts@acme.example"],
  "Cc": [], "Bcc": [], "ReplyTo": [], "Attachments": [],
  "Purpose": "subscription_payment_method_expiring",
  "Language": "de-CH",
  "SubjectDataContext": {
    "DisplayName": "Dana Keller",
    "Brand": "visa",
    "LastFour": "4242",
    "ExpiryMonth": "03",
    "ExpiryYear": "2030",
    "ExpiryDate": "2030-03-31"
  },
  "BodyDataContext": {
    "DisplayName": "Dana Keller",
    "Brand": "visa",
    "LastFour": "4242",
    "ExpiryMonth": "03",
    "ExpiryYear": "2030",
    "ExpiryDate": "2030-03-31"
  },
  "CorrelationId": "card-expiry-<stored method id>-2030-03"
}
```

The subscriber replaces the card through the same `POST /subscriptions/{id}/payment-method/setup`
session used to add a first one; the new card becomes the default once the provider confirms it.

**Starter template.**

`subscription_payment_method_expiring`

```
Subject: Your {{Brand}} card ending {{LastFour}} expires {{ExpiryMonth}}/{{ExpiryYear}}
```

```html
<p>Hello {{DisplayName}},</p>
<p>The <strong>{{Brand}}</strong> card ending <strong>{{LastFour}}</strong> that pays your subscription
expires on <strong>{{ExpiryDate}}</strong>.</p>
<p>To avoid an interruption, add a new card from your subscription page before then. The new card
takes over as soon as it is confirmed.</p>
```

## Languages

The mail module matches `Language` **exactly** and has **no fallback**. A request for `de-CH`
does not use an `en-US` template; it is dropped silently.

| Family | Language sent |
| --- | --- |
| Financial documents | `en-US`, always |
| Usage warning | `en-US`, always |
| Card expiry | billing profile's *Email language*, else the billing account's language, else `en-US` |
| Billing-contact notices | billing profile's *Email language*, else the subscription's creation language, else `en-US` |
| Member notices | member's IAM language, else `en-US` |

So every tenant needs `en-US` templates for all twelve purposes, plus the seven notice purposes
and the card expiry purpose in every language its billing contacts and members have set.

## Why an email did not arrive

Decided on this side, and recorded as a mail delivery report (see [tracing/](../../tracing/)):

| Report source | Outcome / error code | Meaning |
| --- | --- | --- |
| `FinancialDocument` | `NotAttempted` / `document_mail_no_recipient` | Document has no billing contact address. PDF still downloadable. |
| `FinancialDocument` | `NotAttempted` / `document_mail_claim_taken` | An earlier attempt claimed this mail and never recorded its outcome. Nothing resends automatically; use the resend endpoint. |
| `FinancialDocument` | `PublishFailed` / `document_mail_publish_failed` | The bus publish threw. Treated as *unknown*, not failed, because the broker may have accepted it. Operator resend. |
| `SubscriptionNotification` | `NotAttempted` / `subscription_not_found` | The subscription behind the event is gone. |
| `SubscriptionNotification` | `NotAttempted` / `billing_email_missing` | Billing account missing or has no email. |
| `SubscriptionNotification` | `NotAttempted` / `member_email_missing` | IAM gave no address for the member. |
| `SubscriptionNotification` | `NotAttempted` / `seat_change_not_applied` | The seat change the event describes never landed. |
| `UsageThreshold` | *(no report)* | Subscription or billing email missing: logged as a warning only. |
| `PaymentMethodExpiry` | *(no report)* | No billing account charges the card, or the account has no email: logged and the card marked as noticed. An unreadable expiry is logged and left unmarked. |

`Published` means the queue accepted the message, nothing more. Rendering, template lookup, SES
submission, bounces and filtering are the mail module's record, keyed by the same `CorrelationId`.

Decided on the mail module's side, with nothing recorded here:

- no template for that purpose **and** language;
- a template using a `{{Key}}` not in the payload;
- a recipient that is not a registered user of the tenant. An outside accounts-payable mailbox as
  billing contact receives nothing.

## Where it lives

| Thing | File |
| --- | --- |
| Purposes, queue name, default language | `server/Subscription.DomainService/Utilities/SubscriptionConstants.cs` |
| Wire payload | `server/Subscription.DomainService/Messaging/SendMail.cs` |
| Documents | `server/Subscription.DomainService/Services/SubscriptionFinancialDocumentDeliveryService.cs` |
| Notices (billing contact and member) | `server/Subscription.DomainService/Services/SubscriptionNotificationEmailService.cs` |
| Usage warning | `server/Subscription.DomainService/Services/UsageThresholdEmailService.cs` |
| Card expiry | `server/Subscription.DomainService/Services/PaymentMethodExpiryEmailService.cs`, announced by `Scheduling/SubscriptionRepairAnnouncer.cs` |
| Worker consumer for the notices and the warning | `server/Worker/Consumers/Subscription/SubscriptionLifecycleMailConsumer.cs` |
| Language rules | `server/Subscription.DomainService/Utilities/MailLanguage.cs` |
