# Payment secret configuration

Payment provider documents contain configuration and Key Vault references
only. API keys, webhook HMAC keys, return-state keys, shopper-reference keys,
and provider-token encryption keys must be provisioned through the
organization Genesis vault integration.

## PaymentProvider document

Add these fields to each active provider document:

```json
{
  "ProviderCredentialSecretName": "payment-adyen-shared",
  "TenantSecuritySecretName": "payment-tenant-security"
}
```

Do not populate these legacy fields:

```text
ApiKey
ReturnStateHmacKey
PreviousReturnStateHmacKey
StandardWebhookHmacKey
PreviousStandardWebhookHmacKey
TokenWebhookHmacKey
PreviousTokenWebhookHmacKey
ShopperReferenceHmacKey
```

The application ignores those fields during MongoDB reads and never writes
their runtime values back to MongoDB.

## Provider credential secret

The secret named by `ProviderCredentialSecretName` has this JSON shape:

```json
{
  "apiKey": "<provider API key>",
  "standardWebhookHmac": {
    "active": "<active hexadecimal HMAC key>",
    "previous": null
  },
  "tokenWebhookHmac": {
    "active": "<active hexadecimal HMAC key>",
    "previous": null
  }
}
```

Tenants that use the same provider merchant account can reference the same
provider credential secret.

## Tenant security secret

The secret named by `TenantSecuritySecretName` has this JSON shape:

```json
{
  "returnStateHmac": {
    "active": "<base64 random key>",
    "previous": null
  },
  "shopperReferenceHmacKey": "<base64 random key>"
}
```

The shopper-reference key is an identity key. Rotating it changes the derived
shopper reference and therefore requires a planned data migration. Do not
rotate it as an ordinary operational key.

## Stripe provider credential secret

Stripe providers set both `ProviderCredentialSecretName` and `TenantSecuritySecretName`. The
tenant security secret has the same shape as Adyen's and is documented above: it holds this
service's own keys, which sign the return state and derive the shopper reference, so it is
required regardless of provider.

The secret named by `ProviderCredentialSecretName` has this JSON shape:

```json
{
  "secretKey": "sk_live_... or rk_live_...",
  "webhookSigningSecret": {
    "active": "whsec_...",
    "previous": null
  }
}
```

Stripe uses one API key for every call and one signing secret per webhook endpoint. Rolling an
endpoint secret keeps the previous one valid for up to 24 hours, and Stripe sends one signature
per active secret during that window, so populate `previous` for the duration of a roll.

The `PaymentProvider` document needs `ApiBaseUrl` set to `https://api.stripe.com` — no other
host is accepted — and `ProviderName` set to `STRIPE`.

## Provider-token encryption keyring

Both API and Worker require a vault secret named
`PaymentProviderTokenEncryptionKeyRing`:

```json
{
  "activeKeyId": "payment-token-key-2026-07",
  "keys": {
    "payment-token-key-2026-06": "<base64 AES key>",
    "payment-token-key-2026-07": "<base64 AES key>"
  }
}
```

The active key encrypts new provider tokens. Previous keys remain in the
keyring only while records encrypted with them still exist.

## Safe rollout

1. Provision all three vault secrets.
2. Grant the API and Worker identities read access to those exact secrets.
3. Add the two secret-reference fields to every active provider document.
4. Deploy API and Worker.
5. Verify payment creation, callback validation, both webhook types, recurring
   payment, stored-method removal, and refund.
6. Remove the legacy plaintext secret fields from MongoDB.
7. Rotate every API/HMAC key that was previously stored in MongoDB because
   backups may still contain the old values.

Missing, malformed, or inaccessible secrets fail closed. The provider is
treated as unavailable and is not admitted to the in-memory provider cache.
Resolved secrets are held in a bounded local cache only and are never placed
in Redis.

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
