# Payment secret configuration

Provider credentials live on the provider document itself, encrypted with
AES-GCM. Only one piece of material still lives in the vault: the encryption
key ring that protects everything else.

This means the service needs read-only vault access and nothing more. It also
means MongoDB backups contain encrypted payment credentials, so treat those
backups as sensitive: encrypted at rest, access-restricted, retention-bounded.

## Registering a provider

Use `POST /api/payments/providers` rather than writing documents by hand:

```json
{
  "providerName": "STRIPE",
  "merchantId": "acct_1ABC",
  "frontendResultUrl": "https://app.example/payment/result",
  "countryCode": "NL",
  "manualCapture": false,
  "maxRefundDays": 90,
  "apiKey": "sk_live_...",
  "webhookHmacKey": "whsec_..."
}
```

Adyen additionally needs `apiBaseUrl` (its Checkout host varies by environment
and API version) and `tokenHmacKey` (it signs token notifications with a
separate key). Stripe needs neither: its host is fixed and it signs every
event with one secret.

These are derived rather than accepted, and cannot be set through the request:

| Field | Source |
| --- | --- |
| `TenantId` | the caller's execution context |
| `ApiBaseUrl` | the provider descriptor, when the provider has a fixed host |
| `ReturnUrl` | `Payment:PublicBaseUrl` plus `/payments/validate` |
| security keys | generated, unless supplied for a migration |

One configuration is allowed per tenant, provider and merchant, enforced by a
unique index. A duplicate registration is refused rather than creating a
second row.

`Payment:PublicBaseUrl` must be set to this service's own public HTTPS base,
or registration reports itself unavailable.

## What is stored

Credentials are encrypted into two blobs on the provider document:

```text
ProviderSecretsCiphertext         the provider's own credentials
TenantSecuritySecretsCiphertext   this service's return-state and shopper keys
SecretsEncryptionKeyId            which key ring entry encrypted them
```

The plaintext fields on the entity — `ApiKey`, `StandardWebhookHmacKey`,
`ShopperReferenceHmacKey` and the rest — are populated in memory when a
provider is used and are never persisted or serialised out.

The `ProviderCredentialSecretName` and `TenantSecuritySecretName` fields are
legacy. They remain on the entity so older documents still deserialise, but
nothing reads them.

## Credential shapes

Adyen's credential blob:

```json
{
  "apiKey": "<provider API key>",
  "standardWebhookHmac": { "active": "<64 hex characters>", "previous": null },
  "tokenWebhookHmac": { "active": "<64 hex characters>", "previous": null }
}
```

Stripe's credential blob:

```json
{
  "secretKey": "sk_live_... or rk_live_...",
  "webhookSigningSecret": { "active": "whsec_...", "previous": null }
}
```

Both providers share the tenant security blob:

```json
{
  "returnStateHmac": { "active": "<base64, 32 bytes or more>", "previous": null },
  "shopperReferenceHmacKey": "<base64, 32 bytes or more>"
}
```

`shopperReferenceHmacKey` is an identity key. Shopper references are derived
from it and stored payment methods are keyed by those references, so rotating
it changes every derived reference and orphans saved cards. It is not an
ordinary operational key and needs a planned migration.

Rotation is supported through the `previous` slot: both values are accepted
while it is populated. Stripe keeps a rolled webhook secret valid for 24 hours
and sends one signature per active secret during that window.

## Provider-token encryption keyring

This is the one secret that still lives in the vault. Both API and Worker
require a vault secret named `PaymentProviderTokenEncryptionKeyRing`:

```json
{
  "activeKeyId": "payment-token-key-2026-07",
  "keys": {
    "payment-token-key-2026-06": "<base64 AES key>",
    "payment-token-key-2026-07": "<base64 AES key>"
  }
}
```

The active key encrypts new values. Previous keys stay in the ring while
records encrypted with them still exist — provider credentials and stored
payment method tokens alike. Removing a key makes everything it encrypted
unreadable, and those providers stop accepting payments.

`scripts/payment-key-vault/` provisions this key ring.

## Migrating providers that still point at the vault

Providers configured before credentials moved onto the document will not
hydrate, and cannot take payments until migrated. Set:

```json
"Payment": {
  "MigrateProviderSecretsOnStartup": true,
  "TenantIds": [ "<tenant id>" ]
}
```

Start the service, confirm `Provider secret migration completed` reports the
expected counts, then switch it back off. The migration is idempotent, reads
each vault secret and encrypts its JSON verbatim, and skips providers that
already hold encrypted credentials.

Afterwards, confirm an existing stored payment method still resolves. That is
the check nothing automated can do for you: if the shopper reference key did
not carry across, saved cards fail to resolve with no error anywhere.

## Failure behaviour

Missing, malformed or undecryptable credentials fail closed. The provider is
treated as unavailable and is not admitted to the in-memory provider cache,
so payments report `payment_provider_misconfigured` rather than proceeding
with partial configuration. Decrypted values are held in a bounded local
cache only and are never placed in Redis.

# Payment background processing

Payment background work uses the
`blocks_payment_work_listener` command queue as its primary execution path.
Every command carries the tenant identifier required to select the tenant
database. Before publishing or consuming a command, the payment service
establishes a scoped `BlocksContext` for that tenant and restores the previous
context when the operation finishes.

The API dispatches a command immediately after:

- durably accepting a verified webhook;
- atomically appending a payment or refund outbox event; or
- scheduling a recoverable unknown outcome.

Retryable failures schedule another command for the persisted next-attempt
time. MongoDB leases, compare-and-set filters, inbox deduplication, and outbox
deduplication remain authoritative, so duplicate Service Bus delivery is safe.
After webhook inbox persistence succeeds, a command-dispatch failure is logged
but does not reject the webhook; the reconciliation safety net processes the
durable inbox record.

The worker also runs a low-frequency reconciliation safety net. It scans the
configured tenants every `Payment:ReconciliationPollSeconds` seconds, with a
minimum of 60 seconds and a default of 300 seconds. Reconciliation handles the
small failure window where MongoDB commits successfully but command dispatch
fails because MongoDB and Service Bus do not share a transaction.

Production messaging infrastructure must provision
`blocks_payment_work_listener` as a queue consumed by the utility worker. API
instances require send permission and worker instances require receive
permission for that queue.
