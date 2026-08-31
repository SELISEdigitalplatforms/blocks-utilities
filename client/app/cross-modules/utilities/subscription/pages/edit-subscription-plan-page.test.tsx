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

  /**
   * Checked before the subscribed branch, and this is why: an archived plan usually has
   * subscribers, and that branch exists to let a live plan be repriced — the one thing an
   * archived plan may no longer do. Ordered the other way, this page would offer to add a price.
   */
  it("refuses even when it also has subscribers", () => {
    renderPage(plan({ status: "Archived", hasSubscribers: true }));

    expect(
      screen.getByRole("heading", { name: /Archived plans cannot be changed/i }),
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Add another price/i })).not.toBeInTheDocument();
  });
});
