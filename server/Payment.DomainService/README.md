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
