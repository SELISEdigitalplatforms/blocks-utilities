import { getLanguageCode, tStatic } from "@seliseblocks/genesis-os";

const MODULE = "utilities.subscription";

/** Read when the option is shown, never at import time, so a language change is picked up. */
const text = (key: string): string => tStatic(MODULE, key);

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
  {
    value: "all",
    get label() {
      return text("CONSTANTS.DOCUMENT_TYPE_ALL");
    },
  },
  {
    value: "Invoice",
    get label() {
      return text("CONSTANTS.DOCUMENT_TYPE_INVOICE");
    },
  },
  {
    value: "TrialInvoice",
    get label() {
      return text("CONSTANTS.DOCUMENT_TYPE_TRIAL_INVOICE");
    },
  },
  {
    value: "CreditNote",
    get label() {
      return text("CONSTANTS.DOCUMENT_TYPE_CREDIT_NOTE");
    },
  },
] as const;

export const FINANCIAL_DOCUMENT_STATUS_OPTIONS = [
  {
    value: "all",
    get label() {
      return text("CONSTANTS.DOCUMENT_STATUS_ANY");
    },
  },
  {
    value: "Issued",
    get label() {
      return text("CONSTANTS.DOCUMENT_STATUS_ISSUED");
    },
  },
  {
    value: "PartiallyRefunded",
    get label() {
      return text("CONSTANTS.DOCUMENT_STATUS_PARTIALLY_REFUNDED");
    },
  },
  {
    value: "Refunded",
    get label() {
      return text("CONSTANTS.DOCUMENT_STATUS_REFUNDED");
    },
  },
] as const;

/** The sentinel a "no filter" select carries. Never leaves the page layer. */
export const ANY_DOCUMENT_FILTER = "all";

export const FINANCIAL_DOCUMENT_PAGE_SIZE = 25;

export const SUBSCRIPTION_PLAN_CODE_MAX_LENGTH = 64;
export const SUBSCRIPTION_DISPLAY_NAME_MAX_LENGTH = 200;
export const SUBSCRIPTION_KEY_MAX_LENGTH = 64;
export const SUBSCRIPTION_FEATURES_JSON_MAX_LENGTH = 16_384;

export const BILLING_INTERVAL_OPTIONS = [
  {
    value: 0,
    get label() {
      return text("CONSTANTS.INTERVAL_DAY");
    },
  },
  {
    value: 1,
    get label() {
      return text("CONSTANTS.INTERVAL_WEEK");
    },
  },
  {
    value: 2,
    get label() {
      return text("CONSTANTS.INTERVAL_MONTH");
    },
  },
  {
    value: 3,
    get label() {
      return text("CONSTANTS.INTERVAL_YEAR");
    },
  },
] as const;

/**
 * The two answers to "when does this renew". Shown only for a price billed every single month,
 * because that is the only cadence the server will align to a calendar.
 */
export const BILLING_ALIGNMENT_OPTIONS = [
  {
    value: "Anniversary",
    get label() {
      return text("CONSTANTS.ALIGNMENT_ANNIVERSARY");
    },
    get hint() {
      return text("CONSTANTS.ALIGNMENT_ANNIVERSARY_MONTHLY_HINT");
    },
  },
  {
    value: "CalendarMonth",
    get label() {
      return text("CONSTANTS.ALIGNMENT_CALENDAR_MONTH");
    },
    get hint() {
      return text("CONSTANTS.ALIGNMENT_CALENDAR_MONTH_HINT");
    },
  },
] as const;

/**
 * The same choice, worded for a yearly price. The mechanism is identical and the stored value is
 * the same; what differs is what the subscriber gets, and saying "renew on the 1st" without saying
 * "of which month" would read as monthly billing.
 */
export const YEARLY_BILLING_ALIGNMENT_OPTIONS = [
  {
    value: "Anniversary",
    get label() {
      return text("CONSTANTS.ALIGNMENT_ANNIVERSARY");
    },
    get hint() {
      return text("CONSTANTS.ALIGNMENT_ANNIVERSARY_YEARLY_HINT");
    },
  },
  {
    value: "CalendarMonth",
    get label() {
      return text("CONSTANTS.ALIGNMENT_CALENDAR_YEAR");
    },
    get hint() {
      return text("CONSTANTS.ALIGNMENT_CALENDAR_YEAR_HINT");
    },
  },
] as const;

/**
 * When a calendar-aligned yearly price collects its annual amount.
 *
 * Both come to the same money; they differ in when it is taken and what a cancellation during the
 * opening stub leaves the subscriber holding. Worth saying plainly, because an author choosing
 * between them is choosing a refund policy as much as a collection date.
 */
export const CALENDAR_ANNUAL_CHARGE_TIMING_OPTIONS = [
  {
    value: "AtBoundary",
    get label() {
      return text("CONSTANTS.CHARGE_TIMING_AT_BOUNDARY");
    },
    get hint() {
      return text("CONSTANTS.CHARGE_TIMING_AT_BOUNDARY_HINT");
    },
  },
  {
    value: "AtCheckout",
    get label() {
      return text("CONSTANTS.CHARGE_TIMING_AT_CHECKOUT");
    },
    get hint() {
      return text("CONSTANTS.CHARGE_TIMING_AT_CHECKOUT_HINT");
    },
  },
] as const;

export const METER_AGGREGATION_OPTIONS = [
  {
    value: 0,
    get label() {
      return text("CONSTANTS.AGGREGATION_SUM");
    },
  },
  {
    value: 1,
    get label() {
      return text("CONSTANTS.AGGREGATION_MAX");
    },
  },
  {
    value: 2,
    get label() {
      return text("CONSTANTS.AGGREGATION_LAST");
    },
  },
] as const;

export const METER_RESET_POLICY_OPTIONS = [
  {
    value: 0,
    get label() {
      return text("CONSTANTS.RESET_EXPIRES");
    },
  },
  {
    value: 1,
    get label() {
      return text("CONSTANTS.RESET_NONE");
    },
  },
  {
    value: 2,
    get label() {
      return text("CONSTANTS.RESET_ROLLOVER");
    },
  },
] as const;

export const ENTITLEMENT_LIMIT_KIND_OPTIONS = [
  {
    value: 0,
    get label() {
      return text("CONSTANTS.LIMIT_KIND_BOOLEAN");
    },
  },
  {
    value: 1,
    get label() {
      return text("CONSTANTS.LIMIT_KIND_COUNT");
    },
  },
  {
    value: 2,
    get label() {
      return text("CONSTANTS.LIMIT_KIND_UNLIMITED");
    },
  },
] as const;

export const THRESHOLD_PERCENT_PRESETS = [50, 80, 100] as const;

/** Currency name in the UI language; the English name is the fallback when the runtime has no data. */
const currencyName = (code: string, fallback: string): string => {
  try {
    return new Intl.DisplayNames([getLanguageCode()], { type: "currency" }).of(code) ?? fallback;
  } catch {
    return fallback;
  }
};

/**
 * Same currency list Payment offers when authoring a price, kept as its own copy rather than a
 * cross-module import — the two modules are meant to stay independently deployable.
 */
export const SUBSCRIPTION_CURRENCY_OPTIONS = [
  {
    code: "BDT",
    get name() {
      return currencyName("BDT", "Bangladeshi Taka");
    },
  },
  {
    code: "USD",
    get name() {
      return currencyName("USD", "US Dollar");
    },
  },
  {
    code: "EUR",
    get name() {
      return currencyName("EUR", "Euro");
    },
  },
  {
    code: "GBP",
    get name() {
      return currencyName("GBP", "British Pound");
    },
  },
  {
    code: "CHF",
    get name() {
      return currencyName("CHF", "Swiss Franc");
    },
  },
  {
    code: "JPY",
    get name() {
      return currencyName("JPY", "Japanese Yen");
    },
  },
  {
    code: "BHD",
    get name() {
      return currencyName("BHD", "Bahraini Dinar");
    },
  },
  {
    code: "KWD",
    get name() {
      return currencyName("KWD", "Kuwaiti Dinar");
    },
  },
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
