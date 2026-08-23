export const SUBSCRIPTIONS_ENDPOINT = "/api/subscriptions";
export const SUBSCRIPTIONS_CURRENT_ENDPOINT = "/api/subscriptions/current";
export const ENTITLEMENTS_ENDPOINT = "/api/entitlements";
export const SUBSCRIPTION_USAGE_ENDPOINT = "/api/subscription-usage";

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
