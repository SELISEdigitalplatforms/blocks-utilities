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
