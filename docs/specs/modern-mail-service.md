# Modern Mail Service Spec

## Status

Implemented and evolving.

This spec is the source of truth for future mail-service changes. Any behavioral change to mail submission, attachment processing, routing, outbox retry, completion events, delivery tracking, or rate limiting must update this document first.

## Goals

- Send mail through Microsoft Graph using modern app-only authentication.
- Support large attachments without loading large files fully into memory.
- Prevent heavy attachment traffic from delaying lightweight mail.
- Confirm whether the mail provider accepted the submission request.
- Avoid losing mail commands when queue publishing fails.
- Publish project-isolated send completion events.
- Track delivery status separately from provider submission.
- Protect the API, domain, and provider layers from mail flooding.
- Keep the implementation testable and organized by responsibility.

## Non-Goals

- `MailSendCompletedEvent` must not mean final recipient delivery.
- Delivery status must not claim that a user read, opened, or saw the email.
- Middleware rate limiting must not inspect or deserialize the request body.
- Application-side subscriber filtering must not be the only isolation mechanism for project-facing events.

## Main Features

### 1. Microsoft Graph Submission

The service sends mail through Microsoft Graph using app-only authentication.

Details:
- `TenantId`, `SenderUserName`, `AccountPassword`, and `SenderAddress` must be validated before Graph calls.
- Graph sending uses draft-first behavior: create message, add attachments, then send the draft.

### 2. Attachment-Aware Mail Lanes

Every saved mail is routed into exactly one internal lane.

Details:
- `NoAttachment`: no attachments; fastest lane.
- `SmallAttachment`: all attachment sizes are known and every attachment is `<= 3 MB`.
- `LargeAttachment`: any attachment is `> 3 MB`, or any attachment size is unknown.

### 3. Large Attachment Upload

Large attachments use Graph upload sessions.

Details:
- Attachments `<= 3 MB` use normal `FileAttachment` flow.
- Attachments `> 3 MB` use upload-session flow and must respect Graph maximum attachment size limits.

### 4. Outbox-Based Queue Publishing

The API must not directly publish mail send commands to broker queues.

Details:
- Request flow saves `MailToBeSent` and creates a `MailOutboxMessage`.
- After a tenant outbox row is saved, the service publishes a lightweight `ProcessMailOutboxMessageCommand` containing `TenantId` and `OutboxMessageId`.
- The queue-triggered outbox consumer loads the exact outbox row from that tenant database, claims it, and publishes the real command/event.
- Background workers must not rely on request-time `BlocksContext`; they must use stored tenant/project context from queued payloads.
- Tenant sweeping is only a low-frequency recovery path for missed pointer messages, not the normal hot path.
- When Mongo transactions are available, mail and outbox records must be saved in one transaction.
- When Mongo runs as a standalone server and rejects transactions, the repository must fall back to non-transactional writes and compensate by deleting the mail record if outbox insert fails.
- Production environments should run Mongo as a replica set or sharded cluster if strict atomic mail/outbox persistence is required.

### 5. Provider Submission State

Mail submission tracks whether Graph accepted the send request.

Details:
- Successful Graph send means provider submission accepted, not recipient delivery.
- Transient Graph failures are retried; confirmed accepted submissions must never be resent.

### 6. Project-Isolated Completion Events

A completion event is published after the final provider submission result.

Details:
- Event includes `IsSuccess = true` when Graph accepts the submission.
- Event includes `IsSuccess = false` after permanent failure or retry exhaustion.
- Events must publish to a scoped destination using `ProjectKey` when present, otherwise `TenantId`, such as `blocks_email_send_completed_{projectKeyOrTenantId}`.

### 7. Delivery Tracking

Delivery tracking is separate from submission completion.

Details:
- Mail should include the custom header `x-blocks-mail-item-id` when creating the Graph draft.
- Delivery status checks use `IExchangeMessageTraceClient` and may re-check because Exchange status can change later.

### 8. Rate Limiting

The system keeps three independent protection layers.

Details:
- API middleware limiter protects HTTP endpoints from request floods.
- Domain limiter protects recipient-volume quotas.
- Provider limiter protects Graph/mailbox/client submission pressure.

### 9. Email Sends Listing

The API exposes a tenant-scoped email sends list.

Details:
- Response includes sender, subject, language, organization, submission status, and recipients with delivery status.
- Sender, subject, and recipient filters use text search.
- Pagination uses continuation-token style pagination.

### 10. Service Organization

Mail services are organized by responsibility.

Details:
- `Core`: mail orchestration services.
- `Transport`, `Attachments`, `Categories`, `Outbox`, `DeliveryTracking`, `RateLimiting`, and `Concurrency` hold focused implementation groups.

## End-To-End Flow

### Request Acceptance

1. Client calls mail API.
2. API middleware rate limiter applies request-count protection.
3. Controller maps request into mail domain model.
4. Domain mail rate limiter calculates recipient cost.
5. Mail is validated.
6. Mail is saved.
7. Mail category is resolved from attachment metadata.
8. Outbox message is created for the correct lane.
9. API returns accepted/queued result.

### Queue Publishing

1. Normal path consumes `ProcessMailOutboxMessageCommand` with `TenantId` and `OutboxMessageId`.
2. Publisher loads the exact tenant outbox message.
3. Publisher atomically claims the message.
4. Publisher sends it to the configured destination queue.
5. On success, outbox message is marked `Published`.
6. On failure, message is retried with backoff by publishing another pointer command for the next attempt time.
7. After max attempts, message is marked `DeadLettered`.
8. Optional tenant sweep can recover saved outbox rows whose pointer command was never published or never processed.

### Provider Submission

1. Lane-specific consumer receives the send command.
2. Consumer delegates to the shared send service.
3. Send service skips mail already marked `Accepted`.
4. Send service checks provider rate limit.
5. Send service claims the mail submission.
6. Graph client creates a draft.
7. Attachments are added through small or large attachment path.
8. Draft is sent.
9. Submission state is updated.
10. Final completion event is published through outbox.

### Delivery Tracking

1. After accepted submission, delivery check command is scheduled.
2. Delivery consumer waits until the check time.
3. Delivery service queries Exchange trace through `IExchangeMessageTraceClient`.
4. Per-recipient delivery status is mapped and stored.
5. Project-scoped delivery status event is published when status changes.
6. Pending or unknown statuses can be rechecked until retry policy is exhausted.

## Failure Behavior

### Queue Publish Failure

- Mail request must remain saved.
- Outbox message remains retryable until published or dead-lettered.
- Original mail request must not be lost.

### Mongo Transaction Unsupported

- Standalone Mongo servers do not support transactions.
- The repository must detect transaction-not-supported errors and retry persistence without a transaction.
- The fallback writes the mail record first, then the outbox record.
- If the outbox write fails after the mail write, the repository must try to delete the newly inserted mail record and then rethrow.
- This fallback is acceptable for local or non-replica environments, but production should prefer transaction-capable Mongo topology.

### Consumer Crash Before Graph Acceptance

- Mail remains non-accepted.
- Retry may occur according to submission state and retry policy.
- Claiming prevents concurrent duplicate sends.

### Graph Transient Failure

- Retry only transient failures such as `408`, `429`, `500`, `502`, `503`, and `504`.
- Honor provider retry-after values when present.

### Graph Permanent Failure

- Do not retry invalid configuration, invalid request, unauthorized, forbidden, mailbox not found, or attachment too large.
- Mark final failure and publish `MailSendCompletedEvent` with `IsSuccess = false`.

### Event Publish Failure

- Event publishing must go through outbox.
- Event publish failure must not trigger duplicate mail submission.

### Delivery Failure

- Delivery failure is not represented by `MailSendCompletedEvent`.
- Delivery changes are represented by `MailDeliveryStatusChangedEvent`.

## Configuration Contract

### Mail Category

```json
{
  "MailCategory": {
    "LargeAttachmentThresholdInMb": 3
  }
}
```

### Microsoft Graph Mail

```json
{
  "MicrosoftGraphMail": {
    "NoAttachmentMaxConcurrentSends": 15,
    "SmallAttachmentMaxConcurrentSends": 8,
    "LargeAttachmentMaxConcurrentSends": 2,
    "LargeAttachmentMaxConcurrentLargeUploads": 1,
    "MaxSubmissionRetryAttempts": 5,
    "InitialSubmissionRetryDelaySeconds": 30,
    "MaxSubmissionRetryDelaySeconds": 900
  }
}
```

### Mail Outbox

```json
{
  "MailOutbox": {
    "Enabled": true,
    "SweepEnabled": true,
    "TenantIds": [],
    "PollIntervalSeconds": 300,
    "BatchSize": 50,
    "MaxPublishAttempts": 10,
    "InitialRetryDelaySeconds": 10,
    "MaxRetryDelaySeconds": 300
  }
}
```

### API Rate Limiting

```json
{
  "ApiRateLimiting": {
    "Enabled": true,
    "MailSendPermitLimit": 60,
    "MailSendWindowSeconds": 60,
    "MailSendQueueLimit": 0,
    "GeneralPermitLimit": 300,
    "GeneralWindowSeconds": 60,
    "GeneralQueueLimit": 0
  }
}
```

## Acceptance Criteria

- No-attachment mail resolves to `NoAttachment` and publishes to the no-attachment lane.
- Small attachment mail resolves to `SmallAttachment` and uses normal Graph attachment flow.
- Unknown or large attachment size resolves to `LargeAttachment` and uses the protected lane.
- API request flood returns `429` before controller work.
- Recipient-volume abuse is blocked by domain rate limiter.
- Provider pressure requeues without submitting to Graph.
- Queue publish failure is retried by outbox.
- Normal outbox publishing is triggered by `ProcessMailOutboxMessageCommand` and does not scan tenants.
- Low-frequency tenant sweep can recover missed pointer messages without being the hot path.
- Standalone Mongo does not throw from mail/outbox save only because transactions are unsupported.
- Non-transactional fallback compensates mail insert if outbox insert fails.
- Accepted Graph submission marks mail `Accepted` and never resends the same mail.
- Permanent Graph failure publishes final completion event with `IsSuccess = false`.
- Completion events are scoped by project-or-tenant destination.
- Delivery status is checked separately and can be rechecked.
- Mail list endpoint remains tenant scoped and supports text filters.

## Test Map

- Category resolver tests cover no, small, large, and unknown attachment paths.
- Routing tests cover lane destination selection.
- Outbox tests cover publish success, retry, and dead-letter behavior.
- Submission tests cover accepted, retryable, permanent, and already-accepted flows.
- Completion event tests cover success, failure, and project isolation.
- Delivery tests cover correlation, status mapping, and recheck behavior.
- Rate limiter tests cover API partitions, domain recipient cost, and provider pressure.
- Email sends list tests cover tenant scoping, filters, and continuation pagination.

## Change Control

Before changing mail behavior:

1. Update this spec.
2. Add or update acceptance criteria.
3. Implement the smallest code change that satisfies the spec.
4. Add tests mapped to the acceptance criteria.
5. Run targeted builds/tests for affected projects.
## Shared Amazon SES delivery tracking

- MailKit submissions can opt into the organization-wide SES configuration set with
  `AmazonSes__DeliveryTrackingEnabled=true`.
- Production must provide `AmazonSes__ConfigurationSetName`,
  `AmazonSes__NotificationTopicArn`, and
  `AmazonSes__AutomaticallyConfirmSubscription` through secured environment
  configuration.
- SES correlation uses the signed `mailItemId` and `tenantId` message tags. The
  shared SNS callback is `POST /api/mail/providers/ses/events`.
- The callback verifies the SNS signature and configured topic ARN before selecting
  a tenant database. SNS message IDs are claimed in the tenant database to make
  duplicate delivery idempotent.
- SES statuses are pushed into recipient delivery tracking. SES submissions do not
  use Exchange message-trace polling.
- Checked-in configuration keeps SES delivery tracking disabled so placeholder
  infrastructure values cannot affect SMTP submission.

## Redis mail rate limiting

- Domain recipient-volume and provider-submission counters use the organization
  Genesis `ICacheClient` rather than tenant MongoDB collections.
- Counter claims use `GetStringValueAsync` and `AddStringValueAsync` with expiry,
  matching the organization-approved Redis abstraction. Access is serialized per
  key inside each service instance.
- Redis keys include the limiter scope and fixed-window start and expire at the
  window boundary.
- Domain limiter Redis failures fail closed with `MailRateLimiterUnavailable`.
  Provider limiter failures return `ProviderRateLimiterUnavailable`, causing the
  existing outbox retry path to delay submission.
- The current organization cache abstraction does not expose a server-side atomic
  increment. Cross-replica atomicity requires a future atomic increment/claim
  operation in that NuGet package.
