import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";
import type { MeterTerms, SimulatedSubscription } from "../models/subscription-simulation.model";
import { OverageTermsSection } from "./overage-terms-section";

const baseSubscription: SimulatedSubscription = {
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
  meters: [],
  version: 1,
};

const blockedMeter: MeterTerms = {
  meterKey: "screening",
  displayName: "Screenings",
  unitLabel: "screening",
  includedQuantity: 150,
  resetPolicy: "Periodic",
  carryForwardCap: null,
  overageAllowed: false,
  overagePricing: null,
};

const unpricedMeter: MeterTerms = {
  ...blockedMeter,
  overageAllowed: true,
  overagePricing: null,
};

const pricedMeter: MeterTerms = {
  ...blockedMeter,
  overageAllowed: true,
  overagePricing: {
    currencyCode: "CHF",
    tiers: [
      { upToQuantity: 100, unitAmount: "1.00" },
      { upToQuantity: null, unitAmount: "0.80" },
    ],
  },
};

const renderSection = (
  meters: MeterTerms[],
  overrides: Partial<SimulatedSubscription> = {},
) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={client}>
      <OverageTermsSection
        subscription={{ ...baseSubscription, ...overrides, meters }}
        organizationId="org-1"
      />
    </QueryClientProvider>,
  );
};

describe("OverageTermsSection", () => {
  it("renders nothing for a legacy subscription with no meters", () => {
    const { container } = renderSection([]);

    expect(container).toBeEmptyDOMElement();
  });

  it("states plainly that additional usage is blocked", () => {
    renderSection([blockedMeter]);

    expect(screen.getByText(/150 screenings included per month/)).toBeInTheDocument();
    expect(screen.getByText(/Additional usage is blocked/)).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /estimate additional usage/i }),
    ).not.toBeInTheDocument();
  });

  it("warns when overage is allowed but nothing prices it, without an estimate action", () => {
    renderSection([unpricedMeter]);

    expect(
      screen.getByText(/allowed, but no overage price is configured/i),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /estimate additional usage/i }),
    ).not.toBeInTheDocument();
  });

  it("formats finite and unbounded graduated tiers into one readable sentence", () => {
    renderSection([pricedMeter]);

    expect(
      screen.getByText(
        /First 100 additional screenings: CHF 1\.00 each; thereafter CHF 0\.80 each\./,
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /estimate additional usage/i }),
    ).toBeInTheDocument();
  });

  it("maps every meter independently rather than only the first", () => {
    renderSection([{ ...blockedMeter, meterKey: "storage" }, pricedMeter]);

    expect(screen.getByText(/Additional usage is blocked/)).toBeInTheDocument();
    expect(screen.getByText(/First 100 additional screenings/)).toBeInTheDocument();
  });

  it("describes the allowance from the usage cadence, not the billing cadence", () => {
    // Yearly-billed, monthly-metered -- the two are independent, and using the billing interval
    // here would have shown "per year" for an allowance that actually resets every month.
    renderSection([blockedMeter], {
      interval: "Year",
      intervalCount: 1,
      usageInterval: "Month",
      usageIntervalCount: 1,
    });

    expect(screen.getByText(/150 screenings included per month/)).toBeInTheDocument();
    expect(screen.queryByText(/per year/)).not.toBeInTheDocument();
  });

  it("shows the terms for a pending subscription, with estimation unavailable rather than absent", () => {
    renderSection([pricedMeter], { status: "Incomplete" });

    // The terms themselves are still shown -- /current returns them for a pending subscription
    // too, and hiding the whole section would contradict that.
    expect(
      screen.getByText(
        /First 100 additional screenings: CHF 1\.00 each; thereafter CHF 0\.80 each\./,
      ),
    ).toBeInTheDocument();
    // But estimation calls the preview endpoint, which only resolves a live subscription, so the
    // action is unavailable rather than offered and then failing.
    expect(
      screen.queryByRole("button", { name: /estimate additional usage/i }),
    ).not.toBeInTheDocument();
    expect(screen.getByText(/available once the subscription is active/i)).toBeInTheDocument();
  });

  it("offers estimation for every status the preview endpoint actually resolves", () => {
    for (const status of ["Trialing", "Active", "PastDue"] as const) {
      const { unmount } = renderSection([pricedMeter], { status });

      expect(
        screen.getByRole("button", { name: /estimate additional usage/i }),
      ).toBeInTheDocument();

      unmount();
    }
  });
});
