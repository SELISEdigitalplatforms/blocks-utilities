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

import { EditSubscriptionPlanPage } from "./edit-subscription-plan-page";

const plan = (overrides: Partial<SubscriptionPlan> = {}): SubscriptionPlan =>
  ({
    planId: "plan-1",
    code: "pro",
    displayName: "Professional",
    description: null,
    featuresJson: null,
    organizationId: "org-1",
    trialDays: null,
    trialDurationKind: null,
    trialDurationCount: null,
    trialRequiresPaymentMethod: true,
    version: 1,
    hasSubscribers: false,
    quantityItems: [],
    meters: [],
    entitlements: [],
    prices: [],
    trialGrants: [],
    ...overrides,
  }) as SubscriptionPlan;

const renderPage = (subject: SubscriptionPlan) => {
  useSubscriptionPlan.mockReturnValue({
    data: subject,
    isLoading: false,
    isError: false,
    error: null,
  });

  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter>
        <EditSubscriptionPlanPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
};

/**
 * No link points here for an archived plan any more, so this route is reached by typing or
 * following a bookmark. It still has to refuse, because hiding a link is not a guard: every form
 * this page offers would have its submission rejected by the server.
 */
describe("EditSubscriptionPlanPage on an archived plan", () => {
  it("refuses to edit it, and says why", () => {
    renderPage(plan({ status: "Archived" }));

    expect(
      screen.getByRole("heading", { name: /Archived plans cannot be changed/i }),
    ).toBeInTheDocument();
    expect(screen.getByText(/carries on unchanged/i)).toBeInTheDocument();
    expect(screen.getByText(/duplicate the plan/i)).toBeInTheDocument();
  });

  it("shows no form to submit", () => {
    renderPage(plan({ status: "Archived" }));

    expect(screen.queryByRole("button", { name: /Save changes/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Add another price/i })).not.toBeInTheDocument();
  });

  /** An archived plan usually has subscribers, and being subscribed must not reopen it. */
  it("refuses even when it also has subscribers", () => {
    renderPage(plan({ status: "Archived", hasSubscribers: true }));

    expect(
      screen.getByRole("heading", { name: /Archived plans cannot be changed/i }),
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Add another price/i })).not.toBeInTheDocument();
  });
});

/**
 * A subscribed plan opens in the full editor. The author has to be told before saving that the
 * edit is a new version and nobody already subscribed is repriced — otherwise the first worry is
 * that saving changes what existing customers pay.
 */
describe("EditSubscriptionPlanPage on a subscribed plan", () => {
  it("says existing subscribers keep what they bought", () => {
    renderPage(plan({ hasSubscribers: true }));

    expect(screen.getByText(/keeps the terms and price they bought/i)).toBeInTheDocument();
  });

  it("does not say so for a plan nobody has bought", () => {
    renderPage(plan({ hasSubscribers: false }));

    expect(screen.queryByText(/keeps the terms and price they bought/i)).not.toBeInTheDocument();
  });
});
