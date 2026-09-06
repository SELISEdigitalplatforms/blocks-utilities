# API controllers

## Subscription and payment endpoint authorization

Every subscription and payment endpoint is a `[ProtectedEndPoint("<resource>")]`. The framework —
`Blocks.Genesis.ProtectedEndpointAccessHandler`, wired up by `ApplicationConfigurations.ConfigureApi`
— does the whole check: the caller is authenticated, the resource is within its tenant quota
(`ResourceLimits`), and the resource is reachable from one of the caller's `permissions` claims or
`BlocksContext` roles (`Permissions`, scoped by `OrganizationId`). No controller or service reads a
claim or a permission itself.

Webhooks (`POST /api/payments/{provider}/webhooks`, the Adyen routes) and
`GET /api/payments/validate` stay `[AllowAnonymous]`: they are called by the provider, not by a
tenant user, and are authenticated by signature instead.

## Resources this service expects

A resource must exist as a row in the tenant's `Permissions` collection for the matching
`OrganizationId` (or `default`), or every endpoint using it answers 403. Reads and writes are
separated so a support role can be given the `.read` half without the ability to change anything.

| Resource | Endpoints |
| --- | --- |
| `payment.payments.read` | `GET /payments`, `GET /payments/{id}` |
| `payment.payments.manage` | `POST /payments/create`, `POST /payments/recurring-payments` |
| `payment.payment-methods.read` | `GET /payments/payment-methods` |
| `payment.payment-methods.manage` | `DELETE /payments/payment-methods/{id}` |
| `payment.providers.read` | `GET /payments/providers` |
| `payment.providers.manage` | `PUT /payments/providers/{id}`, `POST /payments/providers/{id}/rotate`, `POST /payments/providers` |
| `payment.refunds.read` | `GET /payments/{id}/refunds`, `GET /payments/{id}/refunds/{refundId}` |
| `payment.refunds.manage` | `POST /payments/{id}/refunds` |
| `payment.captures.read` | `GET /payments/{id}/captures/{captureId}` |
| `payment.captures.manage` | `POST /payments/{id}/captures` |
| `payment.encryption.read` | `GET /payments/providers/encryption` |
| `payment.encryption.manage` | `POST /payments/providers/encryption/re-encrypt` |
| `subscription.subscriptions.read` | `GET /subscriptions/current`, `GET /subscriptions/{id}/audit`, every `preview` |
| `subscription.subscriptions.manage` | subscribe, cancel, change plan, change quantity, payment-method setup |
| `subscription.invoices.read` | `GET /subscriptions/invoices`, `GET /subscriptions/invoices/{id}/pdf` |
| `subscription.invoices.manage` | `POST /subscriptions/invoices/{id}/resend` |
| `subscription.entitlements.read` | `GET /entitlements`, `GET /entitlements/{key}` |
| `subscription.plans.read` | `GET /subscription-plans`, `GET /subscription-plans/{id}` |
| `subscription.plans.manage` | create/update/archive plans and prices |
| `subscription.discounts.read` | list, get, `POST /subscription-discounts/preview` |
| `subscription.discounts.manage` | create, update, archive |
| `subscription.usage.read` | `GET /subscription-usage/current`, `POST /subscription-usage/overage/preview` |
| `subscription.usage.manage` | `POST /subscription-usage` |
| `subscription.billing-profile.read` / `.manage` | `GET` / `PUT /subscription-billing-profile` |
| `subscription.merchant-profile.read` / `.manage` | `GET` / `PUT /subscription-merchant-profile` |
| `subscription.simulation.read` | simulation state and data reads |
| `subscription.simulation.manage` | every simulation mutation |
| `subscription.background-work.manage` | dead-letter list, requeue, abandon |

`subscription.background-work.manage` keeps the exact name the removed
`SubscriptionBackgroundWorkOperator` policy required, so an identity provider that already grants it
needs no change.

## What the framework does not decide

Two checks remain in the domain services, because they are not permission checks:

- `PaymentOrganizationScope` — whether a request may *name* an organization other than the caller's
  own. That is a scoping rule about request content, not about who the caller is.
- `SubscriptionSimulationGuard` — whether the caller's own organization is the platform console.
  The simulation harness must never be reachable by a wider audience than the console override
  already is, and no permission grant should be able to widen it.
