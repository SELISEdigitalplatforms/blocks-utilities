export const SUBSCRIPTION_PLANS_ENDPOINT = "/api/subscription-plans";
export const SUBSCRIPTION_PLAN_PRICES_ENDPOINT = "/api/subscription-plans/prices";
export const SUBSCRIPTION_BILLING_PROFILE_ENDPOINT = "/api/subscription-billing-profile";
export const SUBSCRIPTION_MERCHANT_PROFILE_ENDPOINT = "/api/subscription-merchant-profile";
export const SUBSCRIPTION_INVOICES_ENDPOINT = "/api/subscriptions/invoices";

/**
 * The kinds of financial document the ledger holds. "All" is a sentinel rather than an empty string,
 * because Radix refuses an empty SelectItem value and the server refuses a blank filter.
 */
export const FINANCIAL_DOCUMENT_TYPE_OPTIONS = [
  { value: "all", label: "All documents" },
  { value: "Invoice", label: "Invoices" },
  { value: "TrialInvoice", label: "Trial invoices" },
  { value: "CreditNote", label: "Credit notes" },
] as const;

export const FINANCIAL_DOCUMENT_STATUS_OPTIONS = [
  { value: "all", label: "Any status" },
  { value: "Issued", label: "Issued" },
  { value: "PartiallyRefunded", label: "Partially refunded" },
  { value: "Refunded", label: "Refunded" },
] as const;

/** The sentinel a "no filter" select carries. Never leaves the page layer. */
export const ANY_DOCUMENT_FILTER = "all";

export const FINANCIAL_DOCUMENT_PAGE_SIZE = 25;

export const SUBSCRIPTION_PLAN_CODE_MAX_LENGTH = 64;
export const SUBSCRIPTION_DISPLAY_NAME_MAX_LENGTH = 200;
export const SUBSCRIPTION_KEY_MAX_LENGTH = 64;
export const SUBSCRIPTION_FEATURES_JSON_MAX_LENGTH = 16_384;

export const BILLING_INTERVAL_OPTIONS = [
  { value: 0, label: "Day" },
  { value: 1, label: "Week" },
  { value: 2, label: "Month" },
  { value: 3, label: "Year" },
] as const;

/**
 * The two answers to "when does this renew". Shown only for a price billed every single month,
 * because that is the only cadence the server will align to a calendar.
 */
export const BILLING_ALIGNMENT_OPTIONS = [
  {
    value: "Anniversary",
    label: "Subscription anniversary",
    hint: "Renews on the day of the month they subscribed.",
  },
  {
    value: "CalendarMonth",
    label: "Calendar month — renew on the 1st",
    hint: "The first period is prorated to the days remaining in the month.",
  },
] as const;

export const METER_AGGREGATION_OPTIONS = [
  { value: 0, label: "Sum — add up every recorded amount" },
  { value: 1, label: "Max — the highest single recording" },
  { value: 2, label: "Last value — only the most recent recording" },
] as const;

export const METER_RESET_POLICY_OPTIONS = [
  { value: 0, label: "Every allowance period — tokens, requests, generations" },
  { value: 1, label: "Never — persistent capacity such as storage" },
  { value: 2, label: "Carry forward — unused allowance rolls into the next period" },
] as const;

export const ENTITLEMENT_LIMIT_KIND_OPTIONS = [
  { value: 0, label: "Boolean — granted or not, no number attached" },
  { value: 1, label: "Count — a numeric limit, drawn from a meter" },
  { value: 2, label: "Unlimited — always granted, never counted" },
] as const;

export const THRESHOLD_PERCENT_PRESETS = [50, 80, 100] as const;

/**
 * Same currency list Payment offers when authoring a price, kept as its own copy rather than a
 * cross-module import — the two modules are meant to stay independently deployable.
 */
export const SUBSCRIPTION_CURRENCY_OPTIONS = [
  { code: "BDT", name: "Bangladeshi Taka" },
  { code: "USD", name: "US Dollar" },
  { code: "EUR", name: "Euro" },
  { code: "GBP", name: "British Pound" },
  { code: "CHF", name: "Swiss Franc" },
  { code: "JPY", name: "Japanese Yen" },
  { code: "BHD", name: "Bahraini Dinar" },
  { code: "KWD", name: "Kuwaiti Dinar" },
] as const;

/**
 * Radix rejects an empty string as a SelectItem value, so "no specific organization" needs a
 * sentinel. It never leaves the form layer: submit maps it back to an omitted field.
 */
export const TENANT_WIDE_ORGANIZATION = "__tenant_wide__";

/**
 * Which organization's catalogue the portal is looking at, carried in the URL so a plan scoped
 * to one organization stays reachable across navigation, refreshes and shared links. The server
 * honours it for the console only.
 */
export const ORGANIZATION_QUERY_PARAM = "organizationId";

export const ORGANIZATION_PAGE_SIZE = 200;
