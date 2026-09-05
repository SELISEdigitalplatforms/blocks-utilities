import { describe, expect, it } from "vitest";

import type { SubscriptionPlan } from "../models/subscription-plan.model";
import {
  applyCatalogueFilters,
  clearCatalogueFilters,
  countActiveFilters,
  readCatalogueFilters,
  startingPrice,
  summariseCatalogue,
  writeCatalogueFilters,
} from "./plan-catalogue-filters";

const plan = (overrides: Partial<SubscriptionPlan> = {}): SubscriptionPlan =>
  ({
    planId: "plan-1",
    code: "pro",
    displayName: "Professional",
    description: null,
    featuresJson: null,
    organizationId: null,
    trialDays: null,
    trialRequiresPaymentMethod: false,
    version: 1,
    hasSubscribers: false,
    quantityItems: [],
    meters: [],
    entitlements: [],
    prices: [],
    ...overrides,
  }) as SubscriptionPlan;

describe("reading catalogue filters from a URL", () => {
  it("defaults to the active catalogue with nothing in the query string", () => {
    expect(readCatalogueFilters(new URLSearchParams())).toEqual({
      status: "Active",
      search: "",
      sort: "name",
    });
  });

  it("round-trips every control", () => {
    const written = writeCatalogueFilters(new URLSearchParams(), {
      status: "Archived",
      search: "pro",
      sort: "updated",
    });

    expect(readCatalogueFilters(written)).toEqual({
      status: "Archived",
      search: "pro",
      sort: "updated",
    });
  });

  it("leaves unrelated query parameters alone", () => {
    // The organization scope lives in the same query string and is owned by a different control.
    const written = writeCatalogueFilters(
      new URLSearchParams({ organizationId: "org-2" }),
      { status: "All", search: "", sort: "name" },
    );

    expect(written.get("organizationId")).toBe("org-2");
    expect(written.get("status")).toBe("All");
  });

  it("omits a value that is already the default", () => {
    const written = writeCatalogueFilters(new URLSearchParams(), {
      status: "Active",
      search: "",
      sort: "name",
    });

    expect(written.toString()).toBe("");
  });

  /**
   * This parses a hand-editable address bar rather than an API request, so a typo shows the
   * ordinary catalogue instead of an error. The server is the thing that rejects a bad status.
   */
  it("falls back to defaults for values it does not recognise", () => {
    const filters = readCatalogueFilters(
      new URLSearchParams({ status: "Draft", sort: "cheapest" }),
    );

    expect(filters.status).toBe("Active");
    expect(filters.sort).toBe("name");
  });

  it("counts only the controls that are away from their default", () => {
    expect(countActiveFilters({ status: "Active", search: "", sort: "name" })).toBe(0);
    expect(countActiveFilters({ status: "Archived", search: "a", sort: "price" })).toBe(2);
  });

  /**
   * Sort changes the order of what's on screen, not which plans are on it. Counting it as a
   * filter (and resetting it from Clear filters) would misrepresent an ordering choice as a
   * narrowing one.
   */
  it("does not count sort as a filter", () => {
    expect(countActiveFilters({ status: "Active", search: "", sort: "price" })).toBe(0);
  });

  /**
   * The search box is fully controlled from this value. Trimming on read would strip a trailing
   * space the instant it was typed, making the cursor look stuck after "pro ".
   */
  it("keeps a trailing space in the search value instead of trimming it away", () => {
    const filters = readCatalogueFilters(new URLSearchParams({ q: "pro " }));

    expect(filters.search).toBe("pro ");
  });

  it("clears status and search but leaves the current sort alone", () => {
    expect(
      clearCatalogueFilters({ status: "Archived", search: "pro", sort: "price" }),
    ).toEqual({ status: "Active", search: "", sort: "price" });
  });
});

describe("filtering and ordering the catalogue", () => {
  const active = plan({ planId: "a", displayName: "Alpha", status: "Active" });
  const archived = plan({
    planId: "b",
    code: "legacy",
    displayName: "Bravo",
    status: "Archived",
  });
  const noStatus = plan({ planId: "c", displayName: "Charlie" });

  it("shows only active plans by default", () => {
    const result = applyCatalogueFilters([active, archived, noStatus], {
      status: "Active",
      search: "",
      sort: "name",
    });

    expect(result.map((entry) => entry.planId)).toEqual(["a", "c"]);
  });

  it("shows only archived plans under Archived", () => {
    const result = applyCatalogueFilters([active, archived, noStatus], {
      status: "Archived",
      search: "",
      sort: "name",
    });

    expect(result.map((entry) => entry.planId)).toEqual(["b"]);
  });

  it("shows both under All", () => {
    const result = applyCatalogueFilters([active, archived, noStatus], {
      status: "All",
      search: "",
      sort: "name",
    });

    expect(result).toHaveLength(3);
  });

  /**
   * A response cached before the status field existed must read as on sale. Hiding it from every
   * tab would empty the catalogue after a deploy, which is worse than any mislabelled badge.
   */
  it("treats a plan with no status as active", () => {
    const result = applyCatalogueFilters([noStatus], {
      status: "Active",
      search: "",
      sort: "name",
    });

    expect(result).toHaveLength(1);
  });

  it("searches name and code together with the status filter", () => {
    const result = applyCatalogueFilters([active, archived, noStatus], {
      status: "All",
      search: "legacy",
      sort: "name",
    });

    expect(result.map((entry) => entry.planId)).toEqual(["b"]);
  });

  /**
   * The family badge and the description are both visible on the card, so a term the user can
   * see on screen must be able to find the plan, not just its name and code.
   */
  it("also searches family code and description", () => {
    const familyPlan = plan({
      planId: "d",
      displayName: "Delta",
      familyCode: "growth",
      status: "Active",
    });
    const describedPlan = plan({
      planId: "e",
      displayName: "Echo",
      description: "Built for teams scaling fast",
      status: "Active",
    });

    expect(
      applyCatalogueFilters([active, familyPlan, describedPlan], {
        status: "All",
        search: "growth",
        sort: "name",
      }).map((entry) => entry.planId),
    ).toEqual(["d"]);

    expect(
      applyCatalogueFilters([active, familyPlan, describedPlan], {
        status: "All",
        search: "scaling",
        sort: "name",
      }).map((entry) => entry.planId),
    ).toEqual(["e"]);
  });

  it("orders by most recently updated, putting undated plans last", () => {
    const older = plan({
      planId: "older",
      lastUpdatedAtUtc: "2026-01-01T00:00:00Z",
      status: "Active",
    });
    const newer = plan({
      planId: "newer",
      lastUpdatedAtUtc: "2026-08-01T00:00:00Z",
      status: "Active",
    });

    const result = applyCatalogueFilters([older, noStatus, newer], {
      status: "All",
      search: "",
      sort: "updated",
    });

    expect(result.map((entry) => entry.planId)).toEqual(["newer", "older", "c"]);
  });

  /**
   * Compared in major units. On the raw minor-unit integers 500 JPY outranks 4.00 CHF, which is
   * not a currency conversion problem — it is the smallest coin being a different size.
   */
  it("orders by starting price across currencies, putting priceless plans last", () => {
    const yen = plan({
      planId: "yen",
      status: "Active",
      prices: [{ unitAmountMinor: 500, currencyCode: "JPY" }],
    } as Partial<SubscriptionPlan>);
    const franc = plan({
      planId: "franc",
      status: "Active",
      prices: [{ unitAmountMinor: 400, currencyCode: "CHF" }],
    } as Partial<SubscriptionPlan>);

    expect(startingPrice(yen)).toBe(500);
    expect(startingPrice(franc)).toBe(4);

    const result = applyCatalogueFilters([yen, noStatus, franc], {
      status: "All",
      search: "",
      sort: "price",
    });

    expect(result.map((entry) => entry.planId)).toEqual(["franc", "yen", "c"]);
  });
});

describe("summary counts", () => {
  /**
   * Derived from the whole catalogue rather than the filtered list: "12 active" must not change
   * because somebody typed in the search box, or it stops being a fact about the catalogue and
   * becomes a restatement of the list already on screen.
   */
  it("counts the whole catalogue, not the current filter", () => {
    const summary = summariseCatalogue([
      plan({ planId: "a", status: "Active", familyCode: "core" }),
      plan({ planId: "b", status: "Active", familyCode: "core" }),
      plan({ planId: "c", status: "Archived", familyCode: "legacy" }),
      plan({ planId: "d", status: "Archived" }),
      plan({ planId: "e" }),
    ]);

    expect(summary).toEqual({ active: 3, archived: 2, all: 5, families: 2 });
  });
});
