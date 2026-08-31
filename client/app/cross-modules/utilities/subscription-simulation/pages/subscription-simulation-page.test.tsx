import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { SimulatedSubscription } from "../models/subscription-simulation.model";

const { useCurrentSimulatedSubscription } = vi.hoisted(() => ({
  useCurrentSimulatedSubscription: vi.fn(),
}));

vi.mock("../hooks/use-current-simulated-subscription", () => ({
  useCurrentSimulatedSubscription,
}));

vi.mock("../../subscription/hooks/use-subscription-plans", () => ({
  useSubscriptionPlans: () => ({
    data: [],
    error: null,
    isError: false,
    isFetching: false,
    isLoading: false,
    refetch: vi.fn(),
  }),
}));

vi.mock("../hooks/use-quantity-change", () => ({
  useCancelPendingQuantityChange: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

vi.mock("../hooks/use-start-payment-method-setup", () => ({
  useStartPaymentMethodSetup: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

vi.mock("../services/subscription-simulation.service", async () => {
  const actual = await vi.importActual<
    typeof import("../services/subscription-simulation.service")
  >("../services/subscription-simulation.service");

  return {
    ...actual,
    subscriptionSimulationService: {
      getEntitlements: vi.fn().mockResolvedValue({ entitlements: [] }),
    },
  };
});

vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => ({
    data: { organizations: [{ itemId: "org-1", name: "Northwind" }] },
    isError: false,
  }),
}));

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));

import { SubscriptionSimulationPage } from "./subscription-simulation-page";

const meteredSubscription = (
  overrides: Partial<SimulatedSubscription> = {},
): SimulatedSubscription => ({
  subscriptionId: "sub-1",
  status: "Active",
  planCode: "professional",
  planName: "Professional",
  currencyCode: "CHF",
  unitAmountMinor: 8_900,
  interval: "Month",
  intervalCount: 1,
  usageInterval: "Month",
  usageIntervalCount: 1,
  displayPriceNote: null,
  quantities: [],
  currentPeriodStartUtc: "2026-08-01T00:00:00Z",
  currentPeriodEndUtc: "2026-09-01T00:00:00Z",
  nextPaymentAtUtc: "2026-09-01T00:00:00Z",
  trialEndsAtUtc: null,
  cancelAtPeriodEnd: false,
  canceledAtUtc: null,
  pendingQuantityChange: null,
  currentTier: null,
  recurringAmountMinor: 8_900,
  checkoutUrl: null,
  hasPaymentMethod: null,
  meters: [
    {
      meterKey: "screening",
      displayName: "Screenings",
      unitLabel: "screening",
      includedQuantity: 150,
      resetPolicy: "Periodic",
      carryForwardCap: null,
      overageAllowed: true,
      overagePricing: {
        currencyCode: "CHF",
        tiers: [{ upToQuantity: null, unitAmount: "1.00" }],
      },
    },
  ],
  version: 1,
  ...overrides,
});

const renderPage = () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <SubscriptionSimulationPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
};

describe("SubscriptionSimulationPage overage-terms visibility", () => {
  it("shows the overage terms for a pending Incomplete subscription, without an estimate action", async () => {
    useCurrentSimulatedSubscription.mockReturnValue({
      data: meteredSubscription({ status: "Incomplete" }),
      error: null,
      isError: false,
      isLoading: false,
      refetch: vi.fn(),
    });

    renderPage();

    expect(await screen.findByText(/overage terms/i)).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /estimate additional usage/i }),
    ).not.toBeInTheDocument();
    // Incomplete has not paid yet -- the entitlement-gated Usage section stays hidden, unlike
    // the contractual terms above.
    expect(screen.queryByText(/^Usage$/)).not.toBeInTheDocument();
  });

  it("shows both the overage terms and the entitled usage section for an Active subscription", async () => {
    useCurrentSimulatedSubscription.mockReturnValue({
      data: meteredSubscription({ status: "Active" }),
      error: null,
      isError: false,
      isLoading: false,
      refetch: vi.fn(),
    });

    renderPage();

    expect(await screen.findByText(/overage terms/i)).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /estimate additional usage/i }),
    ).toBeInTheDocument();
  });

  it("shows the overage terms for a non-entitled Unpaid subscription, without an estimate action", async () => {
    useCurrentSimulatedSubscription.mockReturnValue({
      data: meteredSubscription({ status: "Unpaid" }),
      error: null,
      isError: false,
      isLoading: false,
      refetch: vi.fn(),
    });

    renderPage();

    expect(await screen.findByText(/overage terms/i)).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /estimate additional usage/i }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/^Usage$/)).not.toBeInTheDocument();
  });
});
