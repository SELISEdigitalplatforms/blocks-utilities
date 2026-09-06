import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { SubscriptionPlan } from "../models/subscription-plan.model";

const { useSubscriptionPlans } = vi.hoisted(() => ({ useSubscriptionPlans: vi.fn() }));

vi.mock("../hooks/use-subscription-plans", () => ({ useSubscriptionPlans }));

vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => ({ data: { organizations: [] }, isError: false }),
}));

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));

import { SubscriptionPlanListPage } from "./subscription-plan-list-page";

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
    status: "Active",
    ...overrides,
  }) as SubscriptionPlan;

const renderPage = (plans: SubscriptionPlan[]) => {
  useSubscriptionPlans.mockReturnValue({
    data: plans,
    error: null,
    isError: false,
    isFetching: false,
    isLoading: false,
    refetch: vi.fn(),
  });

  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter>
        <SubscriptionPlanListPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
};

describe("grouping the catalogue", () => {
  /**
   * Every family-less plan used to key its own section, so a catalogue of standalone plans was
   * one full-width row per plan instead of a shared 3-column grid. Guards against that regression.
   */
  it("puts every family-less plan in one shared grid instead of one section each", () => {
    renderPage([
      plan({ planId: "a", displayName: "Alpha" }),
      plan({ planId: "b", displayName: "Bravo" }),
      plan({ planId: "c", displayName: "Charlie" }),
    ]);

    const cards = screen.getAllByText(/^From |^No price configured yet$/);
    const sharedGrid = cards[0].closest("section")?.querySelector(".grid");

    expect(sharedGrid?.querySelectorAll(":scope > *")).toHaveLength(3);
  });

  /**
   * The heading over a family group must name the family, not whichever level happened to sort
   * first — the first level's own display name is not the family's name.
   */
  it("leads the family heading with the family code, not a level's own name", () => {
    renderPage([
      plan({
        planId: "starter",
        displayName: "Starter",
        familyCode: "growth",
        familyRank: 1,
      }),
      plan({
        planId: "premium",
        displayName: "Premium",
        familyCode: "growth",
        familyRank: 2,
      }),
    ]);

    const familyHeading = screen.getByRole("heading", { name: "growth" });
    const section = familyHeading.closest("section");

    // The group heading names the family; each level still carries its own name on its own
    // card underneath, so both "growth" and "Starter" are on screen, just at different levels.
    expect(familyHeading).toBeInTheDocument();
    expect(section?.querySelector("h3")).toBe(familyHeading);
  });
});
