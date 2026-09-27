export const SUBSCRIPTIONS_ENDPOINT = "/api/subscriptions";
export const SUBSCRIPTIONS_CURRENT_ENDPOINT = "/api/subscriptions/current";
/**
 * The organization's subscriptions to user-wise plans. `current` never returns one of these, so
 * this is how a caller finds the subscription to put people on. Members are then read and written
 * at `${SUBSCRIPTIONS_ENDPOINT}/{id}/members`, composed at the call site.
 */
export const SUBSCRIPTIONS_MEMBER_BASED_ENDPOINT = "/api/subscriptions/member-based";
/**
 * Buyer-facing, read-only validation of a discount code: it prices the code against a plan and
 * price without reserving a redemption or writing anything. A rejected code is data here, not an
 * error — the standard, undiscounted quote comes back alongside the reason.
 */
export const SUBSCRIPTION_DISCOUNTS_PREVIEW_ENDPOINT = "/api/subscription-discounts/preview";
export const ENTITLEMENTS_ENDPOINT = "/api/entitlements";
export const SUBSCRIPTION_USAGE_ENDPOINT = "/api/subscription-usage";
export const SUBSCRIPTION_USAGE_CURRENT_ENDPOINT = "/api/subscription-usage/current";
/**
 * What the caller themselves may spend: their place's allowance where they hold one, the
 * organization's for every other meter. Same body as `current`, one item per meter.
 */
export const SUBSCRIPTION_USAGE_MINE_ENDPOINT = "/api/subscription-usage/mine";
export const SUBSCRIPTION_USAGE_OVERAGE_PREVIEW_ENDPOINT =
  "/api/subscription-usage/overage/preview";

/**
 * The test-harness controller, distinct from the integrator-facing API above: it forces payment
 * outcomes, renewals and background work directly rather than exercising the client-facing
 * surface an integration would call. Console-only and unavailable unless the server has both
 * `SubscriptionSimulation:Enabled` and (for the data console) `:DataConsoleEnabled` turned on.
 */
export const SIMULATION_HARNESS_ENDPOINT = "/api/subscription-simulation";

export const AUDIT_TRAIL_DEFAULT_LIMIT = 100;
/** The server clamps anything above this back down rather than rejecting it. */
export const AUDIT_TRAIL_MAX_LIMIT = 500;
export const AUDIT_TRAIL_LIMIT_OPTIONS = [50, 100, 250, 500] as const;

/**
 * The browser's own IANA zone. Periods turn over on this clock, so simulating from wherever the
 * tester happens to sit is the closest stand-in for a real subscriber's timezone.
 */
export const detectBrowserTimeZone = (): string =>
  Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
