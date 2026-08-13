# Payment secret configuration

Provider credentials live on the provider document itself, encrypted with
AES-GCM. Only one kind of material still lives in the vault: the encryption key
rings that protect everything else, one per tenant and organization.

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

### Which organization the configuration belongs to

`organizationId` is optional. Omit it and the caller's own organization is used, which is
what every registration did before the field existed. Name one and that organization is
used instead — the configuration console runs with a fixed `default` organization, so
without this a tenant could only ever configure that one.

A named organization is verified against IAM (`GET /api/iam/organizations/{id}`) using the
**caller's own bearer token**, so IAM scopes the lookup to the caller's tenant and applies
its own read permission. A caller who cannot see an organization in IAM cannot register a
provider under it. `Payment:IamBaseUrl` must point at IAM, or a request naming an
organization is refused as `organization_verification_unavailable`; requests naming none
never reach IAM and are unaffected.

An organization IAM does not know is refused with `organization_not_found`. An organization
IAM could not be asked about is refused too — "unreachable" is not "does not exist", and
proceeding would write configuration under an unconfirmed organization and encrypt it
against that organization's key ring.

**The organization is identity, not configuration.** It decides which key ring encrypts the
credentials, so it cannot be changed afterwards without re-encrypting them, and the update
endpoint rejects it like `ProviderName` and `MerchantId`.

**Provision that organization's key ring first.** Registration encrypts under it; without
one it fails closed as `payment_registration_unavailable`.

This is the only place an organization is accepted from a request. **Reads deliberately do
not follow.** Payment listing, single-payment fetch and saved-card lookup take the
organization from context only — accepting it from a query would let anyone read another
organization's payments by naming it.

One configuration is allowed per tenant, provider and merchant, enforced by a
unique index. A duplicate registration is refused rather than creating a
second row.

`Payment:PublicBaseUrl` must be set to this service's own public HTTPS base,
or registration reports itself unavailable.

## Managing a provider

List the calling tenant's provider configurations with:

```http
GET /api/payments/providers
```

The response contains `paymentProviderId`, `version`, identity, endpoints,
and editable configuration. It never contains ciphertext, API keys, webhook
secrets, tenant IDs, or shopper-reference keys.

Replace editable configuration with the current version:

```http
PUT /api/payments/providers/{paymentProviderId}
Content-Type: application/json
```

```json
{
  "version": 3,
  "frontendResultUrl": "https://app.example/payment/result",
  "countryCode": "CH",
  "manualCapture": false,
  "maxRefundDays": 90,
  "storeId": null,
  "isEnabled": true
}
```

`ProviderName` and `MerchantId` are identity fields and are rejected by this
endpoint. `ApiBaseUrl` and `ReturnUrl` are also outside its configuration
contract. A version mismatch returns `409`; reload the provider and reapply
the intended change rather than overwriting another administrator's update.

Rotate one or more credentials explicitly:

```http
POST /api/payments/providers/{paymentProviderId}/rotate
Content-Type: application/json
```

```json
{
  "version": 4,
  "apiKey": null,
  "webhookHmacKey": "<new provider webhook secret>",
  "tokenHmacKey": null
}
```

Omitted or `null` values remain unchanged. A new webhook secret becomes
`active`, and the old active value becomes `previous`; both verifiers already
accept both slots. API keys are replaced because provider API authentication
does not use the webhook-signature overlap model. Stripe rejects
`tokenHmacKey`; Adyen HMAC values must contain exactly 64 hexadecimal
characters.

`shopperReferenceHmacKey` is deliberately rejected by the rotation endpoint.
Every successful update or rotation invalidates and refreshes the in-memory
provider cache immediately.

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

## Encryption key rings

This is the one kind of secret that still lives in the vault, and there is one
ring per **tenant and organization** — an organization within a tenant may be a
separate business, so two organizations under one tenant are a trust boundary
rather than an administrative one. A single ring for everything would mean one
compromise exposing every merchant account and every stored card.

The secret name is computed, never looked up:

```
payment-keyring-{tenantSlug}-{organizationSlug}
```

Both slugs come from `PaymentSlug.Create`, which sanitises the identifier and
appends a short hash so two identifiers cannot collide after sanitising. A
tenant-level ring omits the organization fragment; that is its own scope, not a
wildcard, and it serves records written before organizations existed.

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

Rings are read from the vault on first use and cached for
`EncryptionKeyRingCacheSeconds` (default 300), because at startup the service
does not yet know which organizations exist. A rotated ring is therefore picked
up within that window rather than needing a restart. A ring that cannot be read
fails closed for its own organization only.

`scripts/payment-key-vault/Provision-PaymentKeyRing.ps1` creates and rotates
these rings, and remains the only way to rotate a key or remove a retired one.

### Provisioning a new organization

Registration provisions the ring itself when the scope has none, so a new
organization needs no manual step. Requires `Payment:AutoProvisionKeyRing` (on by
default), `KeyVault__KeyVaultUrl` in the environment, and a vault grant of `set`
for the service's identity. Without any of the three it reports
`payment_key_ring_unavailable`, which is the same failure the manual path already
produced, so nothing regresses while a deployment change is pending.

**The application may create a ring and may never modify one.** That asymmetry is
the whole safety argument: creating a ring that does not exist cannot destroy
anything, while replacing an existing ring's active key makes every credential and
stored card encrypted under the old one permanently unreadable. So rotation and
key removal stay with the script, where a human is present and the confirmation
prompts are.

Two details worth knowing, because both are silent if wrong:

- Provisioning is guarded by a distributed lock on the secret name. Two first
  registrations for the same new organization would otherwise both find nothing
  and both write, the second replacing the key the first had just encrypted
  under.
- A newly created ring **carries the shared ring's keys as non-active entries**.
  While `FallBackToSharedEncryptionKeyRing` is on, an unprovisioned scope has been
  writing under the shared key; giving it a ring of its own stops that fallback,
  so without the seeded keys everything it already wrote would become unreadable
  the moment it was provisioned. Re-encryption and removal of the shared key
  remain the operator's job — see the migration section below.

To provision by hand instead — an on-premise vault, or an environment where the
service is deliberately not granted `set`:

```bash
./Provision-PaymentKeyRing.ps1 -VaultName <vault> -TenantId <tenant> -OrganizationId <org>
```

Either way, confirm with `GET /api/payments/providers/encryption`, which reports the
expected secret name and the active key id — never any key material. This is the
replacement for the old startup check, which a per-organization ring makes
impossible.

### Migrating off the shared ring

Existing deployments have one shared ring, `PaymentProviderTokenEncryptionKeyRing`.
`Payment:FallBackToSharedEncryptionKeyRing` (default `true`) lets a scope with no
ring of its own keep using it, so deploying scoped rings breaks nothing. Per
tenant and organization:

1. `./Provision-PaymentKeyRing.ps1 ... -ImportSharedKey` — the shared key goes in
   present but **not** active, so existing records still decrypt while new writes
   use the fresh key.
2. `POST /api/payments/providers/encryption/re-encrypt` — moves that scope's
   provider credentials and saved card tokens onto the active key. Idempotent;
   re-run until it reports nothing re-encrypted.
3. `./Provision-PaymentKeyRing.ps1 ... -RemoveKeyId <shared key id>` — only once
   step 2 has nothing left to move. Any record still naming that key becomes
   permanently unreadable.

Set `FallBackToSharedEncryptionKeyRing` to `false` only after every scope has
been through all three. While it is on, an unprovisioned scope silently keeps
working and nothing forces the isolation this exists to achieve.

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

## Saved cards and off-session charges

Saving a card sends Stripe **both** `saved_payment_method_options.payment_method_save`
and `payment_intent_data.setup_future_usage=off_session`, because Stripe treats
them as separate purposes:

| Parameter | Governs |
| --- | --- |
| `payment_method_save` | Whether Stripe collects the save consent, and whether the card may be shown back to the shopper later (`allow_redisplay: always`) |
| `setup_future_usage` | The mandate that permits charging the card later with nobody present |

Send only the first and saved cards appear at the next checkout but cannot be
charged off-session — Stripe declines with `authentication_required`. Send only
the second and they can be charged but never reappear, with no way to change
that after the fact. Both are gated on the shopper's save consent: without it
neither is sent, since taking a mandate nobody granted is not ours to take.

> Saving payment details engages privacy law. Stripe links the EDPB guidance
> from its `setup_future_usage` documentation; check it with your legal team
> before enabling saved cards in a new jurisdiction.

Charging off-session needs the provider's own payer identifier —
`StoredPaymentMethod.ProviderPayerReference`, Stripe's `cus_…`. It is stored
rather than derived because nothing else holds it, and Stripe refuses a saved
payment method that is not named alongside its customer. A card saved before
that field existed cannot be charged and is rejected as
`provider_payer_reference_missing`; the shopper has to save it again.

Adyen leaves the field null and addresses the shopper by the derived reference
alone.

### Which cards a shopper is offered

Listing matches on **both** the shopper reference and the organization, as a
pair. The organization is the **caller's** — the same value the card is stamped
with when it is saved.

Scoping by the *resolved configuration's* organization instead looks
equivalent and is not. An organization with no configuration of its own
resolves the tenant's, whose organization is null, so every card it saved went
in under its own name and was looked up under none. The cards sat visibly in
the database and the API returned nothing.

The reference alone looks sufficient, because it is an HMAC under each
organization's own key. That only holds while those keys differ. Registration
deliberately accepts an existing `ShopperReferenceHmacKey` so a migration does
not orphan saved cards — and supplying one key to two organizations, which is
the natural thing to do when splitting a tenant, makes their references
collide. The card would then be offered at a merchant account that cannot
charge it, and the shopper sees a decline on a card that looks perfectly good.

So the safety is the organization filter; matching keys are merely tolerated.

## Which payments a caller sees

| Caller | Sees |
| --- | --- |
| Belongs to an organization | That organization's payments, plus payments made before organizations existed |
| Belongs to no organization | Every payment in the tenant |

Payments predating organizations have no organization on them and are the
tenant's shared history. They stay visible to every organization deliberately:
excluding them would empty every console on the day a tenant is split. The
trade is that all organizations see the same pre-migration history, including
each other's old payments. Stamp those rows with an organization if that
matters — the filter narrows automatically once they carry one.

The organization comes from the caller's context, never the request, so nobody
can list another organization's payments by naming it. The same rule applies to
fetching a single payment by id: without it, filtering the list would be
theatre, since identifiers travel in URLs, logs and support tickets. A payment
outside the caller's scope reports `payment_not_found` rather than a forbidden
error, so the response cannot be used to confirm an identifier exists
elsewhere.

## Settings worth knowing about

Everything lives under the `Payment` configuration section. Most values are
tuning with reasonable defaults; these are the ones whose default is a decision
rather than a number.

| Setting | Default | Why it matters |
| --- | --- | --- |
| `PublicBaseUrl` | *(empty)* | This service's own HTTPS base, used to derive the checkout return URL. Empty means provider registration fails with `payment_registration_unavailable`. Not caller-supplied, because a caller-supplied return URL would let a request redirect the payment flow elsewhere. |
| `IamBaseUrl` | *(empty)* | IAM's HTTPS base, used to verify an organization named in a registration. Empty refuses such registrations as `organization_verification_unavailable`; registrations naming no organization are unaffected. |
| `AutoProvisionKeyRing` | `true` | Whether registration creates a missing key ring instead of failing. Create-only — the service never modifies an existing ring. Needs `KeyVault__KeyVaultUrl` in the environment and a vault grant of `set`; without them it reports `payment_key_ring_unavailable`. |
| `FallBackToSharedEncryptionKeyRing` | `true` | Lets an organization with no key ring of its own use the pre-migration shared ring. Set to `false` once every organization is provisioned and re-encrypted — until then, nothing forces the isolation. |
| `EncryptionKeyRingCacheSeconds` | `300` | How long a running process keeps a key ring before re-reading it. A rotated ring is not picked up until this elapses. |
| `EncryptionKeyRingDisposalGraceSeconds` | `60` | How long an evicted ring stays usable before its key bytes are zeroed. Too short and an in-flight operation fails with what looks like data corruption. |
| `MigrateProviderSecretsOnStartup` | `false` | One-shot move of vault-backed credentials onto their documents. Idempotent and safe to leave on, but intended to be switched off once every environment has run it. |
| `CurrencyMinorUnits` | common currencies | A currency absent from this map cannot be charged. Adding a currency means adding it here, in every environment. |
| `TenantIds` | *(empty)* | Which tenants startup migrations run for. Nothing else discovers tenants, so an omitted tenant is silently skipped. |

One value sits outside that section and outside the settings files entirely:
`KeyVault__KeyVaultUrl`, the vault address used to provision key rings. It is read
straight from the environment, deliberately, so a deployment address never lands
in a committed file. Unset, key ring provisioning reports itself unavailable.

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
