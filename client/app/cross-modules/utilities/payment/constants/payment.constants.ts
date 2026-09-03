export const PAYMENT_ENDPOINT = "/api/payments";
export const CREATE_PAYMENT_ENDPOINT = "/api/payments/create";
export const STORED_PAYMENT_METHODS_ENDPOINT =
  "/api/payments/payment-methods";
export const PAYMENT_PROVIDERS_ENDPOINT = "/api/payments/providers";

export const PAYMENT_PAGE_SIZE_OPTIONS = [10, 25, 50, 100] as const;
export const STORED_PAYMENT_METHOD_PAGE_SIZE_OPTIONS = [5, 10, 20] as const;

export const PAYMENT_PROVIDER_SUGGESTIONS = ["ADYEN-ONLINE"] as const;

export const PAYMENT_CURRENCY_OPTIONS = [
  { code: "BDT", name: "Bangladeshi Taka" },
  { code: "USD", name: "US Dollar" },
  { code: "EUR", name: "Euro" },
  { code: "GBP", name: "British Pound" },
  { code: "CHF", name: "Swiss Franc" },
  { code: "JPY", name: "Japanese Yen" },
  { code: "BHD", name: "Bahraini Dinar" },
  { code: "KWD", name: "Kuwaiti Dinar" },
] as const;

export const PAYMENT_STATUS_OPTIONS = [
  "INITIATING",
  "PROCESSING",
  "MAKE_PAYMENT_FAILED",
  "INITIATION_UNKNOWN",
  "AUTHORIZED",
  "REFUSED",
  "PARTIALLY_CAPTURED",
  "CAPTURED",
  "CANCELLED",
  "PARTIALLY_REFUNDED",
  "REFUNDED",
] as const;

export const REFUNDABLE_PAYMENT_STATUSES = new Set([
  "AUTHORIZED",
  "CAPTURED",
  "PARTIALLY_CAPTURED",
  "PARTIALLY_REFUNDED",
]);

export const PAYMENT_FLOW_OPTIONS = [
  { label: "All flows", value: "all" },
  { label: "Hosted checkout", value: "HOSTED_CHECKOUT" },
  { label: "Recurring charge", value: "RECURRING_CHARGE" },
] as const;

export const PAYMENT_LIST_REFRESH_INTERVAL_MS = 15_000;

/**
 * The methods Stripe can charge again later with nobody present.
 *
 * Mirrors `StripePaymentMethodSelection.ReusableOffSession` on the server, which is the set that
 * decides what survives into a checkout that stores the method for renewals. Everything outside it
 * is dropped from such a session by design — a subscription bought with TWINT could never renew
 * itself — so this set is what lets the form warn an operator before they wonder why a method they
 * ticked never appeared on a subscription's first charge.
 *
 * Kept as the whole server set rather than only the methods offered below, so a method configured
 * through the API is badged correctly too.
 */
export const REUSABLE_OFF_SESSION_PAYMENT_METHODS = new Set([
  "card",
  "link",
  "paypal",
  "sepa_debit",
  "us_bank_account",
  "bacs_debit",
  "au_becs_debit",
]);

/**
 * The payment methods the provider form offers to tick.
 *
 * A fixed list rather than free text: the server validates neither of these two fields, so this is
 * the only thing standing between a typo and a checkout Stripe refuses to create. It is a curated
 * subset rather than every method Stripe supports — the ones this product is actually sold with.
 * A method set through the API that is missing here is still shown and preserved, so curating it
 * narrows what can be added, never what can be kept.
 *
 * Order matters: Stripe renders the methods in the order they arrive, and the form submits them in
 * the order they appear here so that what an operator sees is what a shopper gets.
 */
export const PAYMENT_METHOD_OPTIONS = [
  {
    value: "card",
    label: "Card",
    hint: "Apple Pay and Google Pay ride on this.",
  },
  { value: "twint", label: "TWINT", hint: undefined },
  {
    value: "paypal",
    label: "PayPal",
    hint: "Subscriptions need Stripe's recurring approval on the account.",
  },
  { value: "klarna", label: "Klarna", hint: undefined },
  { value: "link", label: "Link", hint: undefined },
  { value: "sepa_debit", label: "SEPA Direct Debit", hint: undefined },
] as const;

/** Whether a method can back a renewal, so the form can say when one cannot. */
export const canBeReusedOffSession = (method: string): boolean =>
  REUSABLE_OFF_SESSION_PAYMENT_METHODS.has(method.trim().toLowerCase());
