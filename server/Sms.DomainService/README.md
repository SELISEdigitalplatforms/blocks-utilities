# SMS Service

## Overview

Sends SMS through a provider each tenant configures (Twilio or Telnyx), tracks delivery per
recipient, and reports status through provider callbacks and scheduled checks.

Behaviours worth knowing before you use it, because they are deliberate:

- **The tenant is always the caller's.** No request carries a tenant or project key; the Api reads
  it from the authenticated context, and the repository refuses to run without one.
- **The provider key never lands in a configuration document.** It is written to Blocks Secrets and
  only the secret id is stored. No endpoint returns it.
- **Sending is asynchronous.** `Send` answers once the message is accepted and queued; the provider
  call happens in the Worker. The response carries the message id, and the outcome arrives as a
  status event.
- **Each recipient has its own outcome.** A message to five numbers can be delivered to four; a
  retry only goes to the recipients still pending.
- **A message is never sent twice in parallel.** A worker must take the message's send lease
  before calling the provider, so a redelivered command or a second replica is a no-op.

## Flow

```
Api  POST /api/Sms/Send
     ├─ validate, spam filter, rate limit        (provider configuration settings)
     ├─ save SmsMessage (Accepted, recipients Pending)      tenant database
     └─ publish SendSmsCommand ──────────────► broker queue blocks_sms_send_listener
            │ publish failed?                        │
            └─► Retry item due now ──┐               ▼
                                     │      Worker  SendSmsConsumer
                                     │               │
                                     ▼               ▼
                  BlocksRootDb.SmsBackgroundWork ──► SmsProcessingService.ProcessSendAsync
                  (Retry, DeliveryCheck)               ├─ schedule watchdog Retry
                        ▲                              ├─ claim send lease
                        │                              ├─ send each Pending recipient
                        └──── schedule retry / check ──┴─ Submitted | RetryScheduled | Failed

Provider ── POST /sms/{provider}/webhooks/{tenantId} ──► verify signature ──► recipient status
```

## Api endpoints

All under `/api/Sms`, authenticated.

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `Send` | Send `MessageText` to `DestinationNumbers`. |
| `POST` | `SendByTemplate` | Render the tenant's `SmsTemplate` (`TemplateName`, `Language`) with `DataContext` (`{{key}}` placeholders) and send it. |
| `POST` | `SaveProviderConfiguration` | Create (no `ConfigurationId`) or update a provider configuration. |
| `GET` | `GetProviderConfiguration` | The active configuration, without the key (`HasApiKey` says whether one is set). |
| `POST` | `SaveTemplate` | Create a template, or update one when `TemplateId` is set. |
| `GET` | `GetTemplate?templateId=` | One template, with its `Placeholders`. |
| `GET` | `GetTemplates?search=&language=&page=&pageSize=` | Templates ordered by name, `pageSize` up to 100. |
| `DELETE` | `DeleteTemplate?templateId=` | Delete a template. |

`Send` and `SendByTemplate` accept an optional `CorrelationId`, carried through logs and events.
Destination numbers must be 7 to 15 digits with an optional `+`; duplicates are dropped.

## Provider configuration

One tenant can hold several configurations. The one used is the enabled one marked default, else the
most recently updated enabled one. Saving a configuration as default clears the flag on the others.

| Field | Notes |
| --- | --- |
| `Name` | Required, up to 100 characters. |
| `ProviderType` | `Twilio` (1) or `Telnyx` (2). |
| `SenderNumber` | E.164 number to send from. Required unless `SenderName` is set. |
| `SenderName` | Optional alphanumeric sender id (1–11 letters, digits or spaces, at least one letter) shown to recipients instead of the number. When set it is always used as the sender. |
| `AccountId` | Twilio account SID (`AC` + 32 hex). Required for Twilio. |
| `ApiKey` | Twilio auth token or Telnyx API key. Required on create; empty on update keeps the stored key, a value rotates it. |
| `MessagingProfileId` | Telnyx messaging profile (GUID). Required for Telnyx. |
| `WebhookPublicKey` | Telnyx account public key, used to verify callbacks. Required for Telnyx. |
| `StatusCallbackBaseUrl` | Public `https` base URL of this Api. Without it, no delivery callbacks are requested and status comes only from scheduled checks. |
| `MaxRetryAttempts` | Send rounds before pending recipients fail. 1–10, default 5. |
| `DeliveryCheckDelayMinutes` | Wait before polling the provider for delivery. 1–1440, default 10. |
| `RateLimit` | See [Rate limits](#rate-limits). |
| `SpamFilter` | See [Spam filter](#spam-filter). |

### Sender name

The provider's From is `SenderName` when one is set, else `SenderNumber`. Alphanumeric sender ids
are one-way (recipients cannot reply) and not accepted everywhere: the US and Canada, among others,
reject them, and the provider fails the send for that recipient. Tenants sending to those countries
should leave `SenderName` empty. On Telnyx the name must also be enabled on the messaging profile.

### Where the key goes

`SaveProviderConfiguration` hands the key to Blocks Secrets (`ISecretService.SetAsync`) as a
`service` secret named `sms-{provider}-{configurationId}`, tagged `sms`, and stores the returned id
as `ApiKeySecretId`. An update with a new key calls `RotateAsync` on the same id, so the id stays
valid. A `service` secret is readable by the tenant's own callers and is shown as metadata only in the
Blocks OS UI, so the save endpoint is the only way to change it.

The Worker and the webhooks have no user, and Blocks Secrets refuses an unauthenticated context even
for a `service` secret. They read the key inside `SmsTenantContext.Enter(tenantId)`: a context
carrying only that tenant id, flagged authenticated, with no roles or permissions, restored on
dispose.

Keys are cached in memory for 5 minutes, keyed by tenant, secret id and the configuration's
`LastUpdatedDate`. A save bumps that date and every caller loads the configuration fresh, so a
rotation takes effect immediately in every process. Failed reads are not cached.

### Rate limits

Fixed-window counters in Redis. The limiter fails closed: if Redis is unreachable, nothing is sent.

| Field | Default | Counts |
| --- | --- | --- |
| `TenantMaxPerWindow` / `TenantWindowSeconds` | 300 / 60 | SMS for the tenant, one per recipient. |
| `RecipientMaxPerWindow` / `RecipientWindowSeconds` | 5 / 300 | SMS to one number. |

Recipient limits are checked first, so a request refused for one flooded number does not spend the
tenant's allowance. A refused request is saved as `Failed` with `sms_rate_limited`.

### Spam filter

| Field | Default | Effect |
| --- | --- | --- |
| `Enabled` | `true` | `false` skips every check below. |
| `MaxRecipients` | 100 | More recipients blocks the message. |
| `MaxMessageLength` | 1000 | Longer raises the risk level to Medium (recorded, not blocked). |
| `UrlPolicy` | `Flag` | A URL is `Allow`ed, `Flag`ged (risk High, still sent) or `Block`ed. |
| `BlockedTerms` | password, bank, wallet, crypto | A URL together with any of these blocks the message, whatever the URL policy. |

A blocked message is saved as `Quarantined` with its reasons in `RiskReasons`, and the request fails.

## Processing

### Sending

The first send travels over the broker (`blocks_sms_send_listener`). `SendSmsConsumer` enters the
tenant's context and calls `ProcessSendAsync`, which:

1. Schedules a **watchdog** Retry item for when the lease would lapse. It comes first: if the worker
   dies or throws after taking the lease, this item reclaims the message.
2. **Claims the send lease** with one `FindOneAndUpdate`: from `Accepted`, `Queued` or
   `RetryScheduled`, or from `Processing` whose lease has expired. No claim, no send.
3. Sends to each recipient still `Pending`, writing that recipient's outcome as soon as it is known,
   so a crash mid-loop cannot lose or repeat it.
4. Ends the round:
   - recipients still pending and attempts left → `RetryScheduled`, Retry item at the backoff time;
   - otherwise pending recipients fail, and the message becomes `Submitted` (at least one recipient
     reached the provider) or `Failed`, and a status event is published.

The consumer rethrows on failure so the broker redelivers; on Service Bus the queue dead-letters after
10 deliveries. Redelivery is safe because of the lease.

If the Api cannot publish to the broker, it schedules a Retry item due now instead, so an accepted
message is not lost to a broker outage.

Twilio clients are built per call from the tenant's key over a pooled `HttpClient`; nothing is
shared between tenants. Telnyx sends carry an idempotency key per message, recipient and attempt.

### Retries

A failure is retried only if it is transient (`SmsTransientErrors`): HTTP 408, 429 or 5xx from either
provider, connection faults, and timeouts. Anything else, including other 4xx and authentication
failures, fails that recipient at once. Backoff is exponential from 30 seconds, capped at 15 minutes,
with jitter, for up to `MaxRetryAttempts` rounds.

### The work queue

Retries and delivery checks run from `SmsBackgroundWork` in the root database (`BlocksRootDb`, or the
secret's `RootDatabaseName`), following the Subscription work-queue pattern:

- one item per (tenant, message, kind), enforced by a unique index; scheduling again moves its due
  time;
- `SmsWorkQueueBackgroundService` claims the earliest due item with `FindOneAndUpdate` and a 5-minute
  lease, enters the tenant's context and runs it;
- completed items are deleted; a handler that throws backs off, and after 10 failures the item is
  marked `Dead` and left for inspection.

Items hold ids only: no numbers, text or keys.

### Delivery status

Two sources, both only ever moving a recipient from `Submitted` to a final state, so a late or
repeated report changes nothing:

- **Callbacks** from the provider, when `StatusCallbackBaseUrl` is set.
- **Delivery checks** polled from the provider `DeliveryCheckDelayMinutes` after a send, repeated up
  to 6 times while any recipient is still unconfirmed.

When every recipient is final, the message becomes `Delivered`, `PartiallyDelivered`, `Undelivered`,
`DeliveryFailed` or `Failed`, and a status event is published.

## Webhooks

`POST /sms/{provider}/webhooks/{tenantId}`: anonymous and outside the `api` prefix, like the payment
webhooks. `provider` is `twilio` or `telnyx`. The URL is built only in `SmsCallbackUrls`, and is
handed to the provider on every send.

The tenant in the route is only a claim until the signature has been checked with that tenant's key.
Nothing is read from the body before then.

| Provider | Verification |
| --- | --- |
| Twilio | `X-Twilio-Signature`, HMAC-SHA1 of the URL and the form fields with the auth token (`Twilio.Security.RequestValidator`). Checked against the URL we registered, not the one the request arrived on, so a proxy rewriting scheme or host does not break it, and a signature for another tenant's URL fails. |
| Telnyx | `telnyx-signature-ed25519` over `{telnyx-timestamp}\|{body}` with the configured public key, within 300 seconds (`Telnyx Webhook.ConstructEvent`). |

| Response | When |
| --- | --- |
| `200` | Verified and applied, or an intermediate status that needs no write. |
| `401` | Missing or invalid signature. |
| `400` | Unreadable body, or a verified body without a message id. |
| `404` | Unknown provider, malformed tenant id, no enabled configuration for that provider, unknown tenant, or no message with that provider id. |

## Messaging

The broker is chosen from `MessageConnectionString`: an `amqp` or `amqps` URI means RabbitMQ,
anything else Azure Service Bus.

| Name | Kind | Purpose |
| --- | --- | --- |
| `blocks_sms_send_listener` | queue | First send of an accepted message. Service Bus max delivery count 10. |
| `blocks_sms_status_topic` | topic | `SmsStatusEvent` when a message reaches `Submitted`, `Failed` or a final delivery state. |

`SmsStatusEvent` carries `MessageId`, `TenantId`, `CorrelationId`, `Provider`, `Status`,
`ErrorCode` and `OccurredAt`.

## Statuses

| Message status | Meaning |
| --- | --- |
| `Accepted` | Saved, not yet queued. |
| `Queued` | On the broker or the work queue. |
| `Processing` | A worker holds the send lease. |
| `RetryScheduled` | Some recipients are waiting for a retry. |
| `Submitted` | Every recipient has an answer from the send, and at least one reached the provider. |
| `Delivered` / `PartiallyDelivered` | All / some recipients delivered. |
| `Undelivered` / `DeliveryFailed` | No recipient delivered; the provider reported undelivered / failed. |
| `Failed` | Nothing reached the provider: no configuration, rate limited, queueing failed, or every recipient failed. |
| `Quarantined` | Blocked by the spam filter. |

Recipients move `Pending` → `Submitted` → `Delivered` / `Undelivered` / `DeliveryFailed`, or to
`Failed` if the send itself failed for good.

## Data

In the tenant's database: `SmsMessages`, `SmsDeliveryAttempts` (one per provider call),
`SmsProviderConfigurations` and `SmsTemplates`. In the root database: `SmsBackgroundWork`.

## Templates

A template is a `Body` with `{{key}}` placeholders, identified by `Name` and `Language` (`en` or
`en-US`); that pair is unique per tenant and is what `SendByTemplate` looks up. Names may use letters,
digits, `_`, `.` and `-`; the body is up to 1600 characters.

Placeholders allow spaces inside the braces and match `DataContext` keys case-insensitively.
`SendByTemplate` refuses to send when any placeholder has no value, and names the missing keys,
rather than texting a raw `{{key}}`. The template view lists `Placeholders` so the portal can show
which values a send needs.

## Wiring

- `RegisterAllSmsApplicationServices()` in both the Api and the Worker. It needs `AddBlocksSecrets`,
  which `RegisterUtilityServices` already calls.
- Worker: `IConsumer<SendSmsCommand>` → `SendSmsConsumer`, and the hosted
  `SmsWorkQueueBackgroundService`.
- `SmsConstants.GetMessageConfiguration` is merged into both hosts' combined message configuration.
