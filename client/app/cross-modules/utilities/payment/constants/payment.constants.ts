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
