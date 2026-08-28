import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { SubscriptionPlan } from "../models/subscription-plan.model";

const { useSubscriptionPlan } = vi.hoisted(() => ({ useSubscriptionPlan: vi.fn() }));

vi.mock("../hooks/use-subscription-plan", () => ({ useSubscriptionPlan }));

vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => ({
    data: { organizations: [{ itemId: "org-1", name: "AmLora Test Org" }] },
    isError: false,
  }),
}));

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));

import { SubscriptionPlanDetailPage } from "./subscription-plan-detail-page";

const plan = (overrides: Partial<SubscriptionPlan> = {}): SubscriptionPlan => ({
  planId: "plan-1",
  code: "personal101",
  displayName: "Personal - 101",
  description: null,
  featuresJson: null,
  organizationId: "org-1",
  trialDays: null,
  trialRequiresPaymentMethod: true,
  version: 1,
  hasSubscribers: false,
  quantityItems: [],
  meters: [
    {
      meterKey: "ses-signatures",
      displayName: "Simple Signatures (SES)",
      unitLabel: "signature",
      aggregation: "Sum",
      includedQuantity: 150,
      overageAllowed: true,
      thresholdPercents: [80],
      rateTables: [],
    },
  ],
  entitlements: [
    {
      key: "ses-signatures",
      limitKind: "Count",
      limit: 150,
      meterKey: "ses-signatures",
      unitLabel: "signature",
    },
  ],
  prices: [
    {
      priceId: "price-abc-123",
      currencyCode: "EUR",
      unitAmountMinor: 300,
      interval: "Month",
      intervalCount: 1,
      quantityItemKey: null,
    },
  ],
  trialGrants: [],
  ...overrides,
});

const renderPage = (subscriptionPlan: SubscriptionPlan) => {
  useSubscriptionPlan.mockReturnValue({
    data: subscriptionPlan,
    isLoading: false,
    isError: false,
    error: null,
  });

  // A real client rather than a mocked mutation hook: the page retires a price through one,
  // and stubbing that away would let the wiring break without a test noticing.
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter>
        <SubscriptionPlanDetailPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
};

/**
 * These guard one recurring failure rather than the page's looks: every identifier an integrator
 * has to send back was missing from the page that exists to tell them what to send. Finding a
 * meter key meant reading the API — the exact errand this portal was built to remove.
 */
describe("SubscriptionPlanDetailPage identifiers", () => {
  // Asserted through the copy control rather than the text: this plan names its entitlement
  // after its meter, so "ses-signatures" legitimately appears three times on the page.
  it("shows the meter key every usage call has to carry", () => {
    renderPage(plan());

    expect(screen.getByText("Meter key")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Copy Meter key ses-signatures" }),
    ).toBeInTheDocument();
  });

  it("shows the price id subscribing has to name", () => {
    renderPage(plan());

    expect(screen.getByText("price-abc-123")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Copy Price id price-abc-123" }),
    ).toBeInTheDocument();
  });

  it("shows which meter an entitlement draws down", () => {
    renderPage(plan());

    expect(screen.getByText("Draws down")).toBeInTheDocument();
  });

  it("says nothing about a drawn-down meter for an entitlement that counts nothing", () => {
    renderPage(
      plan({
        entitlements: [
          {
            key: "priority-support",
            limitKind: "Boolean",
            limit: null,
            meterKey: null,
            unitLabel: null,
          },
        ],
      }),
    );

    expect(screen.queryByText("Draws down")).not.toBeInTheDocument();
  });
});

/**
 * Purely a label, in both directions: naming a predecessor never migrated a subscriber and
 * never changed either plan's editability, so these only check that the link itself renders —
 * not that anything about purchasability changed.
 */
describe("SubscriptionPlanDetailPage plan history", () => {
  it("links to the plan this one replaces", () => {
    renderPage(
      plan({ predecessorPlanId: "plan-0", predecessorDisplayName: "Legacy professional" }),
    );

    const link = screen.getByRole("link", { name: "Replaces Legacy professional →" });
    expect(link).toBeInTheDocument();
    expect(link.getAttribute("href")).toContain("plan-0");
  });

  it("links to the plan that replaced this one", () => {
    renderPage(
      plan({ successorPlanId: "plan-2", successorDisplayName: "Professional (2026)" }),
    );

    const link = screen.getByRole("link", { name: "Replaced by Professional (2026) →" });
    expect(link).toBeInTheDocument();
    expect(link.getAttribute("href")).toContain("plan-2");
  });

  it("shows neither link for a plan with no history", () => {
    renderPage(plan());

    expect(screen.queryByText(/^Replaces /)).not.toBeInTheDocument();
    expect(screen.queryByText(/^Replaced by /)).not.toBeInTheDocument();
  });
});

/**
 * One way in, and it has to be open for the plans that need it most.
 *
 * There used to be two entry points to adding a price: this page's own "Add price" button, which
 * went to a form that had drifted — no billing alignment, no tax, no automatic discount — and the
 * plan editor's price section. Editing is now the only one. That consolidation is only safe
 * because the editor stopped turning subscribed plans away: the server refuses to change a
 * subscribed plan's terms but never refuses a new price, and repricing a live plan is exactly
 * add-the-new-price-then-retire-the-old-one.
 */
describe("SubscriptionPlanDetailPage price management", () => {
  it("offers no second way to add a price", () => {
    renderPage(plan());

    expect(screen.queryByRole("link", { name: "Add price" })).not.toBeInTheDocument();
  });

  it("sends an unsubscribed plan to the editor to be edited", () => {
    renderPage(plan({ hasSubscribers: false }));

    const link = screen.getByRole("link", { name: "Edit" });
    expect(link.getAttribute("href")).toContain("plan-1/edit");
  });

  it("sends a subscribed plan to the same editor, for its prices", () => {
    renderPage(plan({ hasSubscribers: true }));

    // Not disabled, which is what it used to be. A subscribed plan is the one that can need a new
    // price, and the label says which half of the editor is still open to it.
    const link = screen.getByRole("link", { name: "Manage prices" });
    expect(link.getAttribute("href")).toContain("plan-1/edit");
    expect(screen.queryByRole("link", { name: "Edit" })).not.toBeInTheDocument();
  });

  it("points a plan with no price at the editor rather than a deleted route", () => {
    renderPage(plan({ prices: [] }));

    const link = screen.getByRole("link", { name: "Add price" });
    expect(link.getAttribute("href")).toContain("plan-1/edit");
    expect(link.getAttribute("href")).not.toContain("prices/create");
  });
});
