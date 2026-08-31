import type {
  PlanCatalogueFilterName,
  SubscriptionPlan,
} from "../models/subscription-plan.model";
import { toMajorUnits } from "./subscription-format";

/**
 * How the catalogue is ordered.
 *
 * Kept as names rather than comparator functions so the choice survives a round trip through the
 * query string: a shared link has to reproduce the list its sender was looking at.
 */
export type PlanCatalogueSort = "name" | "updated" | "price";

export const PLAN_SORT_LABELS: Record<PlanCatalogueSort, string> = {
  name: "Name",
  updated: "Recently updated",
  price: "Lowest starting price",
};

export const PLAN_STATUS_TABS: PlanCatalogueFilterName[] = [
  "Active",
  "Archived",
  "All",
];

/** The query-string keys. Named here so the page and its tests cannot disagree about them. */
export const CATALOGUE_QUERY_PARAMS = {
  status: "status",
  search: "q",
  sort: "sort",
} as const;

export type PlanCatalogueFilters = {
  status: PlanCatalogueFilterName;
  search: string;
  sort: PlanCatalogueSort;
};

export const DEFAULT_CATALOGUE_FILTERS: PlanCatalogueFilters = {
  status: "Active",
  search: "",
  sort: "name",
};

/**
 * Reads the filters out of a URL.
 *
 * Every unrecognised value falls back to its default rather than being reported, because this
 * parses a hand-editable address bar rather than an API request: a typo in a shared link should
 * show the ordinary catalogue, not an error page.
 */
export const readCatalogueFilters = (
  parameters: URLSearchParams,
): PlanCatalogueFilters => {
  const status = parameters.get(CATALOGUE_QUERY_PARAMS.status);
  const sort = parameters.get(CATALOGUE_QUERY_PARAMS.sort);

  return {
    status: PLAN_STATUS_TABS.find((tab) => tab === status) ?? "Active",
    search: parameters.get(CATALOGUE_QUERY_PARAMS.search)?.trim() ?? "",
    sort:
      sort === "updated" || sort === "price" || sort === "name" ? sort : "name",
  };
};

/**
 * Writes the filters back, leaving anything else in the URL alone.
 *
 * A value equal to its default is removed rather than written, so the ordinary view has a clean
 * address and "no parameters" and "every parameter at its default" cannot describe the same list
 * two different ways.
 */
export const writeCatalogueFilters = (
  parameters: URLSearchParams,
  filters: PlanCatalogueFilters,
): URLSearchParams => {
  const next = new URLSearchParams(parameters);

  const apply = (key: string, value: string, fallback: string) => {
    if (value && value !== fallback) {
      next.set(key, value);

      return;
    }

    next.delete(key);
  };

  apply(CATALOGUE_QUERY_PARAMS.status, filters.status, "Active");
  apply(CATALOGUE_QUERY_PARAMS.search, filters.search, "");
  apply(CATALOGUE_QUERY_PARAMS.sort, filters.sort, "name");

  return next;
};

/** How many filters are away from their default, for the "N active" badge and Clear. */
export const countActiveFilters = (filters: PlanCatalogueFilters): number =>
  (filters.status === "Active" ? 0 : 1) +
  (filters.search === "" ? 0 : 1) +
  (filters.sort === "name" ? 0 : 1);

const isArchived = (plan: SubscriptionPlan) => plan.status === "Archived";

/**
 * The cheapest price on a plan, in major units, or null when it has none.
 *
 * Compared in major units rather than minor because a plan priced in yen and one priced in francs
 * are otherwise ranked by the size of their smallest coin: 500 JPY would sort above 4.00 CHF on
 * the raw integers. This is still not a currency conversion and does not pretend to be one — it
 * only stops the ordering being obviously wrong for the common case of one currency per catalogue.
 */
export const startingPrice = (plan: SubscriptionPlan): number | null => {
  const cheapest = plan.prices.reduce<SubscriptionPlan["prices"][number] | null>(
    (best, price) =>
      best === null ||
      toMajorUnits(price.unitAmountMinor, price.currencyCode) <
        toMajorUnits(best.unitAmountMinor, best.currencyCode)
        ? price
        : best,
    null,
  );

  return cheapest
    ? toMajorUnits(cheapest.unitAmountMinor, cheapest.currencyCode)
    : null;
};

/**
 * Applies the status filter to plans the server has already narrowed.
 *
 * The management catalogue fetches `All` once and filters here, so switching tabs is instant and
 * the summary counts are always consistent with what the tabs show. The server filter is still the
 * one that matters for correctness: this can only ever narrow what it already returned.
 */
const matchesStatus = (
  plan: SubscriptionPlan,
  status: PlanCatalogueFilterName,
): boolean => {
  if (status === "All") {
    return true;
  }

  // A plan with no status at all is treated as active. Absent means a response cached before the
  // field existed, and the alternative — hiding it from every tab — would empty the catalogue.
  return status === "Archived" ? isArchived(plan) : !isArchived(plan);
};

export const applyCatalogueFilters = (
  plans: SubscriptionPlan[],
  filters: PlanCatalogueFilters,
): SubscriptionPlan[] => {
  const term = filters.search.trim().toLowerCase();

  const matching = plans.filter(
    (plan) =>
      matchesStatus(plan, filters.status) &&
      (term === "" ||
        plan.displayName.toLowerCase().includes(term) ||
        plan.code.toLowerCase().includes(term)),
  );

  const sorted = [...matching];

  if (filters.sort === "name") {
    return sorted.sort((left, right) =>
      left.displayName.localeCompare(right.displayName),
    );
  }

  if (filters.sort === "updated") {
    // A plan with no timestamp sorts last rather than first: an older cached response is not news,
    // and putting it at the top would claim it had just changed.
    return sorted.sort(
      (left, right) =>
        new Date(right.lastUpdatedAtUtc ?? 0).getTime() -
        new Date(left.lastUpdatedAtUtc ?? 0).getTime(),
    );
  }

  // Priceless plans sort last, for the same reason: "from nothing" is not the cheapest offer, it
  // is an unfinished plan, and leading the list with it buries the ones that can be sold.
  return sorted.sort((left, right) => {
    const leftPrice = startingPrice(left);
    const rightPrice = startingPrice(right);

    if (leftPrice === null) {
      return rightPrice === null ? 0 : 1;
    }

    return rightPrice === null ? -1 : leftPrice - rightPrice;
  });
};

/**
 * The counts behind the summary cards.
 *
 * Derived from the unfiltered set on purpose: "12 active" must not change because somebody typed
 * in the search box, or the number stops being a fact about the catalogue and becomes a restatement
 * of the list already on screen.
 */
export const summariseCatalogue = (plans: SubscriptionPlan[]) => ({
  active: plans.filter((plan) => !isArchived(plan)).length,
  archived: plans.filter(isArchived).length,
  families: new Set(
    plans
      .map((plan) => plan.familyCode)
      .filter((code): code is string => Boolean(code)),
  ).size,
});
