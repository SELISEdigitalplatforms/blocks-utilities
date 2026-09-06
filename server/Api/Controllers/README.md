# API controllers

## Subscription and payment endpoint authorization

Every subscription and payment endpoint is a `[ProtectedEndPoint("<resource>")]`. The framework —
`Blocks.Genesis.ProtectedEndpointAccessHandler`, wired up by `ApplicationConfigurations.ConfigureApi`
— does the whole check: the caller is authenticated, the resource is within its tenant quota
(`ResourceLimits`), and the resource is reachable from one of the caller's `permissions` claims or
`BlocksContext` roles (`Permissions` collection, scoped by `OrganizationId`). No controller or
domain service reads a claim or a permission itself.

Webhooks (`POST /api/payments/{provider}/webhooks`, the Adyen routes) and
`GET /api/payments/validate` stay `[AllowAnonymous]`: the provider calls them, not a tenant user,
and they authenticate by signature instead.

## Resource naming

`service::controller::name`, matching the other Blocks services (`blocks-data::file::get-file`,
`blocks-os::secret::rotate`). The service segment is this service's name as registered in
`Program.cs` — `blocks-utilities`. The controller segment is the controller, singular and
kebab-cased. The name segment is the action.

Reads and writes are separated so a support role can be granted the `read` half of an area without
the ability to change anything.

## Resources this service expects

Each of these must exist as a permission in Blocks OS (**Identity & Access → Permissions → New**)
with type **Endpoint** and group **blocks-utilities**, and be assigned to the roles that need it.
A resource with no row answers 403 for every endpoint that names it.

| Resource | Endpoints |
| --- | --- |
| `blocks-utilities::payment::read` | `GET /payments`, `GET /payments/{id}` |
| `blocks-utilities::payment::manage` | `POST /payments/create`, `POST /payments/recurring-payments` |
| `blocks-utilities::payment::read-refund` | `GET /payments/{id}/refunds`, `GET /payments/{id}/refunds/{refundId}` |
| `blocks-utilities::payment::manage-refund` | `POST /payments/{id}/refunds` |
| `blocks-utilities::payment::read-capture` | `GET /payments/{id}/captures/{captureId}` |
| `blocks-utilities::payment::manage-capture` | `POST /payments/{id}/captures` |
| `blocks-utilities::payment-method::read` | `GET /payments/payment-methods` |
| `blocks-utilities::payment-method::manage` | `DELETE /payments/payment-methods/{id}` |
| `blocks-utilities::payment-provider::read` | `GET /payments/providers` |
| `blocks-utilities::payment-provider::manage` | `PUT /payments/providers/{id}`, `POST /payments/providers/{id}/rotate`, `POST /payments/providers` |
| `blocks-utilities::payment-provider::read-encryption` | `GET /payments/providers/encryption` |
| `blocks-utilities::payment-provider::manage-encryption` | `POST /payments/providers/encryption/re-encrypt` |
| `blocks-utilities::subscription::read` | `GET /subscriptions/current`, `GET /subscriptions/{id}/audit`, every `preview` |
| `blocks-utilities::subscription::manage` | subscribe, cancel, change plan, change quantity, payment-method setup |
| `blocks-utilities::subscription::read-invoice` | `GET /subscriptions/invoices`, `GET /subscriptions/invoices/{id}/pdf` |
| `blocks-utilities::subscription::manage-invoice` | `POST /subscriptions/invoices/{id}/resend` |
| `blocks-utilities::entitlement::read` | `GET /entitlements`, `GET /entitlements/{key}` |
| `blocks-utilities::subscription-plan::read` | `GET /subscription-plans`, `GET /subscription-plans/{id}` |
| `blocks-utilities::subscription-plan::manage` | create/update/archive plans and prices |
| `blocks-utilities::subscription-discount::read` | list, get, `POST /subscription-discounts/preview` |
| `blocks-utilities::subscription-discount::manage` | create, update, archive |
| `blocks-utilities::subscription-usage::read` | `GET /subscription-usage/current`, `POST /subscription-usage/overage/preview` |
| `blocks-utilities::subscription-usage::manage` | `POST /subscription-usage` |
| `blocks-utilities::subscription-billing-profile::read` | `GET /subscription-billing-profile` |
| `blocks-utilities::subscription-billing-profile::manage` | `PUT /subscription-billing-profile` |
| `blocks-utilities::subscription-merchant-profile::read` | `GET /subscription-merchant-profile` |
| `blocks-utilities::subscription-merchant-profile::manage` | `PUT /subscription-merchant-profile` |
| `blocks-utilities::subscription-simulation::read` | simulation state and data reads |
| `blocks-utilities::subscription-simulation::manage` | every simulation mutation |
| `blocks-utilities::subscription-background-work::manage` | dead-letter list, requeue, abandon |

The last one replaces the `SubscriptionBackgroundWorkOperator` policy, which hand-required a
`permission` claim of `subscription.background-work.manage`. That claim value no longer does
anything; the permission above is what grants the endpoints now.

## What the framework does not decide

Two checks remain in the domain services, because they are not permission checks:

- `PaymentOrganizationScope` — whether a request may *name* an organization other than the caller's
  own. That is a scoping rule about request content, not about who the caller is.
- `SubscriptionSimulationGuard` — whether the caller's own organization is the platform console.
  The simulation harness must never be reachable by a wider audience than the console override
  already is, and no permission grant should be able to widen it.
