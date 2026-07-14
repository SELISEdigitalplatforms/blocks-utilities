# Sms.DomainService Specification

## Purpose

`Sms.DomainService` provides tenant-aware SMS submission, provider dispatch, retry recovery, delivery status updates, and status event publishing for Blocks utilities.

The API accepts SMS requests and performs validation, rate limiting, risk checks, persistence, and durable queue offload. Provider calls are performed by worker consumers, not inside the API request path.

## Scope

In scope:

- Direct SMS send requests.
- Template-based SMS send requests.
- Tenant/project scoped provider configuration.
- Twilio and Telnyx provider dispatch.
- Queue-based send processing.
- Outbox-based recovery and retry scheduling.
- Provider webhook handling.
- Delivery-status reconciliation fallback.
- Redis-backed rate limiting.
- Suspicious message checks.
- SMS status event publishing.

Out of scope for the current implementation:

- Multi-provider weighted routing.
- Provider-specific country compliance management.
- Telnyx final delivery polling; Telnyx delivery updates currently rely on webhooks.
- Admin UI for provider configuration.

## Projects

- `Sms.DomainService`: SMS domain logic, provider integrations, repositories, validators, and DTOs.
- `Api`: HTTP endpoints for SMS requests, configuration, and provider webhooks.
- `Worker`: queue consumers and background recovery jobs.
- `XUnitTest`: unit tests for SMS behavior.

## API Endpoints

Base path is `/api/Sms`.

### Send SMS

`POST /api/Sms/Send`

Request:

```json
{
  "projectKey": "project-key",
  "destinationNumbers": ["+8801700000000"],
  "messageText": "Hello from Blocks",
  "correlationId": "optional-correlation-id"
}
```

Success response:

- `202 Accepted`
- Body contains the SMS mutation response with `MessageId`.
- This means the message was accepted and queued, not delivered.

Failure responses:

- `400 Bad Request` for validation or malformed request failures.
- `422 Unprocessable Entity` for suspicious/blocked content.
- `429 Too Many Requests` for rate limit failures.
- `503 Service Unavailable` for queue/configuration failures.

### Send SMS By Template

`POST /api/Sms/SendByTemplate`

Request:

```json
{
  "projectKey": "project-key",
  "destinationNumbers": ["+8801700000000"],
  "templateName": "login_otp",
  "language": "en-US",
  "dataContext": {
    "code": "123456"
  },
  "correlationId": "optional-correlation-id"
}
```

Success response:

- `202 Accepted`

Failure responses are the same as direct send, with `404 Not Found` when the requested template is not found.

### Save Provider Configuration

`POST /api/Sms/SaveProviderConfiguration`

Creates or updates the active SMS provider configuration for the tenant/project.

Request fields:

- `projectKey`
- `configurationId`
- `name`
- `providerType`: `Twilio` or `Telnyx`
- `isDefault`
- `isEnabled`
- `sender`
- `accountId`
- `authToken`
- `messagingProfileId`
- `statusCallbackBaseUrl`
- `maxRetryAttempts`
- `rateLimitMaxPerWindow`
- `rateLimitWindowSeconds`
- `deliveryCheckDelayMinutes`

Responses:

- `201 Created` when creating a new configuration.
- `200 OK` when updating an existing configuration.

### Get Provider Configuration

`GET /api/Sms/GetProviderConfiguration?projectKey=project-key`

Responses:

- `200 OK` when an active configuration exists.
- `404 Not Found` when no active configuration exists.

### Twilio Webhook

`POST /api/Sms/Webhook/Twilio`

Provider callback endpoint. Twilio calls this URL when status changes if `StatusCallbackBaseUrl` is configured.

Success response:

- `204 No Content`

### Telnyx Webhook

`POST /api/Sms/Webhook/Telnyx`

Provider callback endpoint. Telnyx calls this URL when status changes if `StatusCallbackBaseUrl` is configured.

Success response:

- `204 No Content`

## Tenant Context

Repository calls are tenant-scoped.

- API and queue consumers pass tenant context through `BlocksContext` or command payloads.
- Standalone background jobs pass `tenantId` explicitly because they run outside a request or queue message tenant context.
- Repository methods accept nullable `tenantId`; if omitted, they resolve from `BlocksContext`.
- If neither explicit tenant id nor context tenant id exists, repository calls fail fast.

## Validation

Direct SMS validation:

- `DestinationNumbers` must contain at least one number.
- Each destination number must match `^\+?[0-9]{7,15}$`.
- `MessageText` is required and must be at most 1600 characters.

Template SMS validation:

- `DestinationNumbers` rules are the same as direct SMS.
- `TemplateName` is required.
- `Language` is required.
- `DataContext` must not be null.

## Rate Limiting

Rate limiting is Redis-backed through Genesis `ICacheClient`.

Limits are configured per provider configuration:

- `RateLimitMaxPerWindow`, default `30`.
- `RateLimitWindowSeconds`, default `60`.

The limiter applies two counters:

- Tenant/project window counter.
- Tenant/project/recipient window counter.

Recipient keys are hashed before logging or storing in Redis. The limiter fails closed: if Redis/rate-limit processing fails, the SMS request is blocked.

## Suspicious Message Checks

The suspicious message service assigns risk before queueing.

Current checks:

- More than 100 recipients is blocked.
- Body longer than 1000 characters is medium risk.
- URL in message is high risk.
- URL combined with sensitive terms is blocked.

Sensitive terms currently include:

- `password`
- `otp`
- `bank`
- `wallet`
- `crypto`

Blocked messages must not be queued for provider dispatch.

## Queue Topology

Queues:

- `blocks_sms_outbox_process_listener`
- `blocks_sms_send_listener`
- `blocks_sms_delivery_check_listener`

Topic:

- `blocks_sms_status_topic`

Message types:

- `ProcessSmsOutboxMessageCommand`
- `SendSmsCommand`
- `SmsDeliveryCheckEvent`
- `SmsStatusEvent`

`SendSmsCommand` is a command, not a domain event. It instructs the worker to perform SMS sending.

## Send Flow

1. API receives `Send` or `SendByTemplate` request.
2. API resolves tenant/project.
3. API validates the request.
4. API loads active provider configuration.
5. API runs suspicious message checks.
6. API runs Redis rate limiting.
7. API creates `SmsMessage` with status `Accepted`.
8. API creates `SmsOutboxMessage` with status `Pending`.
9. API sends `ProcessSmsOutboxMessageCommand` to `blocks_sms_outbox_process_listener`.
10. API marks the SMS message as `Queued`.
11. API returns `202 Accepted` with `MessageId`.

The API does not call Twilio or Telnyx directly.

## Outbox Processing Flow

`SmsOutboxProcessConsumer` consumes `ProcessSmsOutboxMessageCommand`.

Processing rules:

1. Load the outbox by `OutboxMessageId` and tenant id.
2. Ignore completed outbox records.
3. Ignore failed outbox records.
4. If `NextVisibleAt` is in the future, log and return.
5. Claim the outbox atomically with `TryClaimOutboxAsync`.
6. If claim succeeds, publish `SendSmsCommand` to `blocks_sms_send_listener`.
7. If command publishing fails, schedule retry or mark failed.

Atomic claiming prevents duplicate processing when delayed queue and background recovery both see the same outbox.

## Provider Send Flow

`SendSmsConsumer` consumes `SendSmsCommand`.

Processing rules:

1. Load the `SmsMessage` by `MessageId` and tenant id.
2. Ignore messages already `Submitted` or `Delivered`.
3. Load the exact outbox record when `OutboxMessageId` exists.
4. Load active provider configuration.
5. Select provider via provider factory.
6. Mark message as `Processing`.
7. Increment attempt count.
8. Call provider boundary.
9. Persist `SmsDeliveryAttempt`.
10. On provider success, mark message `Submitted` and outbox `Completed`.
11. Publish `SmsStatusEvent` with `Submitted` status.
12. Schedule `SmsDeliveryCheckEvent` after `DeliveryCheckDelayMinutes`.

Provider exceptions must not escape the provider boundary. Providers return typed `SmsProviderResult`.

## Retry Flow

Retries are scheduled through new outbox records instead of reusing the failed outbox record.

On transient provider failure:

1. Current outbox is marked `Failed`.
2. Message is marked `Queued` with sanitized error details.
3. New retry outbox is created with incremented `RetryCount`.
4. New outbox `NextVisibleAt` is set by retry policy.
5. New `ProcessSmsOutboxMessageCommand` is scheduled with delayed enqueue time.

Retry policy uses capped exponential backoff with jitter.

## LastQueuedAt Recovery

`LastQueuedAt` records when an outbox process command was successfully queued.

Background recovery only picks stale outbox records. This avoids duplicate processing when the delayed queue is working normally.

A stale outbox is eligible when:

- Status is `Pending` or `RetryScheduled`.
- `NextVisibleAt <= utcNow`.
- Either:
  - `LastQueuedAt <= utcNow - QueueRecoveryGracePeriod`, or
  - `LastQueuedAt` is null and `NextVisibleAt <= utcNow - QueueRecoveryGracePeriod`.

Default grace period:

- `SmsBackgroundProcessing:QueueRecoveryGraceSeconds = 120`

This means the delayed queue gets the first chance. The background service rescues only records that remain unprocessed after the grace period.

## Background Jobs

`SmsBackgroundProcessingService` lives in `Worker/BackgroundJobs`.

Responsibilities:

- Recover stale due outbox records.
- Reconcile old submitted messages.

It is not the primary sender. Normal sends and retries are queue-driven.

Configuration:

```json
{
  "SmsBackgroundProcessing": {
    "TenantIds": [],
    "PollIntervalSeconds": 60,
    "QueueRecoveryGraceSeconds": 120
  }
}
```

`TenantIds` are required for background processing because the service runs outside request and queue tenant context.

## Delivery Tracking

Delivery status can be updated in two ways.

### Provider Webhook

Twilio/Telnyx call the API webhook when configured with `StatusCallbackBaseUrl`.

For Twilio, callback URL is:

```text
{StatusCallbackBaseUrl}/api/Sms/Webhook/Twilio
```

For Telnyx, callback URL is:

```text
{StatusCallbackBaseUrl}/api/Sms/Webhook/Telnyx
```

Provider webhooks update final delivery status idempotently.

### Scheduled Delivery Check

After provider submission, the worker schedules `SmsDeliveryCheckEvent`.

`NotBeforeUtc` means the worker must not query provider delivery status before that time. If the command arrives early, it logs and returns.

This protects against early queue delivery and avoids premature provider polling.

Current provider behavior:

- Twilio supports delivery polling through provider message fetch.
- Telnyx polling currently returns non-final `Submitted`; Telnyx final status relies on webhook.

## Status Events

The service publishes a single status event shape for SMS status changes.

Topic:

```text
blocks_sms_status_topic
```

Payload includes:

- `MessageId`
- `TenantId`
- `ProjectKey`
- `CorrelationId`
- `Provider`
- `ProviderMessageId`
- `Status`
- `ErrorCode`

The service should not publish separate success/failure event types. Consumers should inspect `Status`.

## Provider Rules

### Twilio

Supported sender sources:

- Messaging Service SID through `MessagingProfileId` starting with `MG`.
- E.164 sender number, for example `+15551234567`.
- Numeric short code, 3 to 10 digits.
- Alphanumeric sender ID up to 11 characters, containing at least one letter, and using letters, numbers, or spaces.

Examples:

- `SELISE APP` is valid.
- `SELISE` is valid.
- `SELISE123` is valid.
- `SELISE Signature` is invalid because it exceeds 11 characters.
- `SELISE_APP` is invalid because `_` is not allowed by local validation.

Twilio remains authoritative. If Twilio rejects the sender, the provider returns permanent failure `twilio_invalid_sender`.

### Telnyx

Telnyx uses:

- `Sender` as `From`.
- Optional `MessagingProfileId` when it is a GUID.
- Optional webhook URL from `StatusCallbackBaseUrl`.

## Logging And Security

Logging rules:

- Log `MessageId`, `TenantId`, `ProjectKey`, `CorrelationId`, provider, status, and retry count.
- Mask destination phone numbers.
- Hash recipient values in rate-limit logs.
- Sanitize provider error messages before persistence.
- Do not log provider auth tokens.
- Do not log full message bodies.

Failure rules:

- No silent catches.
- Provider exceptions are translated to typed provider results.
- Queue publish failures are logged and reflected in outbox status.
- Rate limiter failures fail closed.

## Data Entities

### SmsMessage

Represents the logical SMS request and lifecycle.

Important fields:

- `ItemId`
- `TenantId`
- `ProjectKey`
- `CorrelationId`
- `DestinationNumbers`
- `MessageText`
- `TemplateName`
- `Language`
- `DataContext`
- `ProviderType`
- `ProviderMessageId`
- `Status`
- `RiskLevel`
- `RiskReasons`
- `AttemptCount`
- `LastErrorCode`
- `LastErrorMessage`

### SmsOutboxMessage

Represents a durable send/retry scheduling unit.

Important fields:

- `ItemId`
- `MessageId`
- `TenantId`
- `ProjectKey`
- `CorrelationId`
- `Status`
- `RetryCount`
- `MaxRetryCount`
- `NextVisibleAt`
- `LastQueuedAt`
- `LastError`

### SmsDeliveryAttempt

Represents one provider send attempt.

Used for debugging and audit trail.

### SmsProviderConfiguration

Stores provider-specific tenant/project configuration.

Important fields:

- `ProviderType`
- `Sender`
- `AccountId`
- `AuthToken`
- `MessagingProfileId`
- `StatusCallbackBaseUrl`
- `MaxRetryAttempts`
- `RateLimitMaxPerWindow`
- `RateLimitWindowSeconds`
- `DeliveryCheckDelayMinutes`

## Status Lifecycle

Typical successful flow:

```text
Accepted -> Queued -> Processing -> Submitted -> Delivered
```

Possible failure flows:

```text
Accepted -> Queued -> Processing -> Queued -> Processing -> Submitted
Accepted -> Queued -> Processing -> Failed
Submitted -> Undelivered
Submitted -> DeliveryFailed
Accepted -> Quarantined
```

Outbox lifecycle:

```text
Pending -> Processing -> Completed
Pending -> Processing -> Failed
RetryScheduled -> Processing -> Completed
RetryScheduled -> Processing -> Failed
```

## Operational Notes

- Local provider webhooks require a public tunnel; providers cannot call `localhost`.
- `StatusCallbackBaseUrl` must be public when webhook delivery tracking is required.
- Background processing requires tenant ids in worker configuration.
- Redis must be available for SMS sends because the rate limiter fails closed.
- Delayed queue support is required for scheduled outbox processing and delivery checks.

## Verification

Recommended checks:

```powershell
dotnet build D:\Selise\Repos\Blocks\blocks-utilities\server\Sms.DomainService\Sms.DomainService.csproj --no-restore
dotnet build D:\Selise\Repos\Blocks\blocks-utilities\server\Worker\Worker.csproj --no-restore
dotnet test D:\Selise\Repos\Blocks\blocks-utilities\server\XUnitTest\XUnitTest.csproj --filter Sms
```