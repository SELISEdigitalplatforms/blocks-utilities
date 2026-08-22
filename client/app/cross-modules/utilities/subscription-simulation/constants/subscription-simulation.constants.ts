export const SUBSCRIPTIONS_ENDPOINT = "/api/subscriptions";
export const SUBSCRIPTIONS_CURRENT_ENDPOINT = "/api/subscriptions/current";
export const ENTITLEMENTS_ENDPOINT = "/api/entitlements";
export const SUBSCRIPTION_USAGE_ENDPOINT = "/api/subscription-usage";

/**
 * The browser's own IANA zone. Periods turn over on this clock, so simulating from wherever the
 * tester happens to sit is the closest stand-in for a real subscriber's timezone.
 */
export const detectBrowserTimeZone = (): string =>
  Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
