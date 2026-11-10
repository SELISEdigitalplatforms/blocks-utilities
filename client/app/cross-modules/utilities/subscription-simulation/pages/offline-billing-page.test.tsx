import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import type { SimulatedSubscription } from "../models/subscription-simulation.model";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";

const activate = vi.fn();
const reprice = vi.fn();
let current: SimulatedSubscription | null = null;
let plans: Partial<SubscriptionPlan>[] = [];

vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => ({ data: { organizations: [{ itemId: "org-1", name: "Klax Customer AG" }] } }),
}));
vi.mock("../hooks/use-current-simulated-subscription", () => ({
  useCurrentSimulatedSubscription: () => ({ data: current, isLoading: false, isError: false }),
}));
vi.mock("../../subscription/hooks/use-subscription-plans", () => ({
  useSubscriptionPlans: () => ({ data: plans, isLoading: false }),
}));
vi.mock("../hooks/use-offline-billing", () => ({
  useActivateOfflineSubscription: () => ({ mutateAsync: activate, isPending: false }),
  useRepriceOfflineSubscription: () => ({ mutateAsync: reprice, isPending: false }),
}));

const { OfflineBillingPage } = await import("./offline-billing-page");

const renderPage = (organizationId = "org-1") =>
  render(
    <MemoryRouter initialEntries={[`/app/subscription/offline?organizationId=${organizationId}`]}>
      <OfflineBillingPage />
    </MemoryRouter>,
  );

const offlineSubscription = {
  subscriptionId: "sub-1",
  status: "Active",
  planCode: "pro",
  planName: "Pro",
  currencyCode: "CHF",
  collectionMethod: "OfflineInvoice",
  offlinePricingMode: "Contract",
  contractAmountMinor: 50_000,
  pendingOfflinePricingMode: null,
  pendingContractAmountMinor: null,
} as SimulatedSubscription;

/**
 * The tenant admin's page for bank transfer customers (spec 006). What it sends is what every record
 * invoice of the contract will state, so the requests are the substance here.
 */
describe("OfflineBillingPage", () => {
  beforeEach(() => {
    activate.mockReset().mockResolvedValue(offlineSubscription);
    reprice.mockReset().mockResolvedValue(offlineSubscription);
    current = null;
    plans = [
      {
        planId: "plan-1",
        code: "pro",
        displayName: "Pro",
        subscriberScope: 0,
        prices: [
          { priceId: "price-1", currencyCode: "CHF", unitAmountMinor: 9_900, interval: "Month", intervalCount: 1 },
        ] as SubscriptionPlan["prices"],
        quantityItems: [],
      },
    ];
  });

  it("asks for an organization before offering anything, since a contract is never the console's own", () => {
    render(
      <MemoryRouter initialEntries={["/app/subscription/offline"]}>
        <OfflineBillingPage />
      </MemoryRouter>,
    );

    expect(screen.queryByTestId("offline-activation-form")).not.toBeInTheDocument();
  });

  it("refuses a contract without an amount instead of sending one the server would reject", async () => {
    renderPage();
    fireEvent.click(screen.getByRole("combobox", { name: "Plan" }));
    fireEvent.click(await screen.findByRole("option", { name: "Pro" }));
    fireEvent.click(screen.getByRole("combobox", { name: "Pricing" }));
    fireEvent.click(await screen.findByRole("option", { name: "Contract price" }));
    fireEvent.click(screen.getByRole("button", { name: "Activate without payment" }));

    expect(await screen.findByText("Enter a contract amount greater than zero.")).toBeInTheDocument();
    expect(activate).not.toHaveBeenCalled();
  });

  it("activates for the chosen organization with the contract amount in minor units", async () => {
    renderPage();
    fireEvent.click(screen.getByRole("combobox", { name: "Plan" }));
    fireEvent.click(await screen.findByRole("option", { name: "Pro" }));
    fireEvent.click(screen.getByRole("combobox", { name: "Pricing" }));
    fireEvent.click(await screen.findByRole("option", { name: "Contract price" }));
    fireEvent.change(screen.getByLabelText(/Contract amount/), { target: { value: "500" } });
    fireEvent.click(screen.getByRole("button", { name: "Activate without payment" }));

    await waitFor(() => expect(activate).toHaveBeenCalledTimes(1));
    expect(activate.mock.calls[0][0]).toMatchObject({
      organizationId: "org-1",
      planCode: "pro",
      priceId: "price-1",
      pricing: { mode: 1, contractAmountMinor: 50_000, contractCurrencyCode: "CHF" },
    });
  });

  it("re-prices a live offline subscription from the next renewal", async () => {
    current = offlineSubscription;
    renderPage();

    expect(screen.getByTestId("billed-by-invoice")).toBeInTheDocument();
    const amount = screen.getByLabelText(/Contract amount/);
    expect(amount).toHaveValue(500);
    fireEvent.change(amount, { target: { value: "600" } });
    fireEvent.click(screen.getByRole("button", { name: "Schedule from next renewal" }));

    await waitFor(() => expect(reprice).toHaveBeenCalledTimes(1));
    expect(reprice.mock.calls[0][0]).toEqual({
      subscriptionId: "sub-1",
      organizationId: "org-1",
      pricing: { mode: 1, contractAmountMinor: 60_000, contractCurrencyCode: "CHF" },
    });
  });

  it("offers nothing for an organization paying by card, since switching is out of scope", () => {
    current = { ...offlineSubscription, collectionMethod: "AutomaticCharge", offlinePricingMode: null };
    renderPage();

    expect(screen.queryByTestId("offline-repricing-form")).not.toBeInTheDocument();
    expect(screen.queryByTestId("offline-activation-form")).not.toBeInTheDocument();
    expect(screen.queryByTestId("billed-by-invoice")).not.toBeInTheDocument();
  });
});
