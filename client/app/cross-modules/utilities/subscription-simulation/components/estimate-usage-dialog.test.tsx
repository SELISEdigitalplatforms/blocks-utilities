import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { MeterTerms, UsageOveragePreviewResult } from "../models/subscription-simulation.model";

const previewUsageOverage = vi.fn();

vi.mock("../services/subscription-simulation.service", async () => {
  const actual = await vi.importActual<
    typeof import("../services/subscription-simulation.service")
  >("../services/subscription-simulation.service");

  return {
    ...actual,
    subscriptionSimulationService: {
      previewUsageOverage: (...args: unknown[]) => previewUsageOverage(...args),
    },
  };
});

import { EstimateUsageDialog } from "./estimate-usage-dialog";

const meter: MeterTerms = {
  meterKey: "screening",
  displayName: "Screenings",
  unitLabel: "screening",
  includedQuantity: 150,
  resetPolicy: "Periodic",
  carryForwardCap: null,
  overageAllowed: true,
  overagePricing: {
    currencyCode: "CHF",
    tiers: [
      { upToQuantity: 100, unitAmount: "1.00" },
      { upToQuantity: null, unitAmount: "0.80" },
    ],
  },
};

const previewResult: UsageOveragePreviewResult = {
  meterKey: "screening",
  unitLabel: "screening",
  currencyCode: "CHF",
  periodKey: "2026-08",
  periodStartUtc: "2026-08-01T00:00:00Z",
  periodEndUtc: "2026-09-01T00:00:00Z",
  calculatedAtUtc: "2026-08-16T12:00:00Z",
  includedQuantity: 150,
  currentUsage: 140,
  currentOverage: 0,
  additionalQuantity: 20,
  projectedUsage: 160,
  projectedOverage: 10,
  currentCharge: { grossMinor: 0, automaticDiscountMinor: 0, netMinor: 0, taxMinor: 0, totalMinor: 0 },
  additionalCharge: {
    grossMinor: 1_000,
    automaticDiscountMinor: 0,
    netMinor: 1_000,
    taxMinor: 77,
    totalMinor: 1_077,
  },
  projectedPeriodCharge: {
    grossMinor: 12_000,
    automaticDiscountMinor: 0,
    netMinor: 12_000,
    taxMinor: 924,
    totalMinor: 12_924,
  },
  additionalTierBreakdown: [
    { fromOverageQuantity: 0, toOverageQuantity: 10, units: 10, unitAmountMinor: 100, amountMinor: 1_000 },
  ],
  discount: { automaticBasisPoints: 0, promotionalCodeApplied: false },
  tax: { rateBasisPoints: 770, mode: "Exclusive" },
  writesUsage: false,
  chargesPayment: false,
  finalChargeDependsOnActualPeriodEndUsage: true,
};

const renderDialog = () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={client}>
      <EstimateUsageDialog
        meter={meter}
        organizationId="org-1"
        open
        onOpenChange={() => {}}
      />
    </QueryClientProvider>,
  );
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe("EstimateUsageDialog", () => {
  it("does not call the preview for a zero or fractional quantity", async () => {
    renderDialog();

    fireEvent.change(screen.getByLabelText(/additional screenings to estimate/i), {
      target: { value: "0" },
    });
    fireEvent.click(screen.getByRole("button", { name: /^estimate$/i }));

    expect(screen.getByText(/whole number of additional units greater than zero/i)).toBeInTheDocument();
    expect(previewUsageOverage).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText(/additional screenings to estimate/i), {
      target: { value: "2.5" },
    });
    fireEvent.click(screen.getByRole("button", { name: /^estimate$/i }));

    expect(previewUsageOverage).not.toHaveBeenCalled();
  });

  it("calls the preview with a positive whole-number quantity", async () => {
    previewUsageOverage.mockResolvedValue(previewResult);

    renderDialog();
    fireEvent.change(screen.getByLabelText(/additional screenings to estimate/i), {
      target: { value: "20" },
    });
    fireEvent.click(screen.getByRole("button", { name: /^estimate$/i }));

    await waitFor(() =>
      expect(previewUsageOverage).toHaveBeenCalledWith({
        meterKey: "screening",
        additionalQuantity: 20,
        organizationId: "org-1",
      }),
    );
  });

  it("displays the discount, tax and totals the preview reports", async () => {
    previewUsageOverage.mockResolvedValue(previewResult);

    renderDialog();
    fireEvent.change(screen.getByLabelText(/additional screenings to estimate/i), {
      target: { value: "20" },
    });
    fireEvent.click(screen.getByRole("button", { name: /^estimate$/i }));

    await waitFor(() => expect(screen.getByText(/10.77/)).toBeInTheDocument());
    expect(screen.getByText(/7\.7%, Exclusive/)).toBeInTheDocument();
  });

  it("states that previewing neither records usage nor charges payment, and that the final invoice may differ", async () => {
    previewUsageOverage.mockResolvedValue(previewResult);

    renderDialog();
    fireEvent.change(screen.getByLabelText(/additional screenings to estimate/i), {
      target: { value: "20" },
    });
    fireEvent.click(screen.getByRole("button", { name: /^estimate$/i }));

    await waitFor(() =>
      expect(
        screen.getByText(/neither records usage nor charges payment/i),
      ).toBeInTheDocument(),
    );
    expect(screen.getByText(/final invoice depends on actual usage/i)).toBeInTheDocument();
  });

  it("surfaces a preview failure without hiding the meter's own contractual terms", async () => {
    previewUsageOverage.mockRejectedValue(new Error("No overage rate is configured."));

    renderDialog();
    fireEvent.change(screen.getByLabelText(/additional screenings to estimate/i), {
      target: { value: "20" },
    });
    fireEvent.click(screen.getByRole("button", { name: /^estimate$/i }));

    await waitFor(() =>
      expect(screen.getByText(/No overage rate is configured\./)).toBeInTheDocument(),
    );
    // The dialog itself, and the meter it is estimating for, remain on screen -- a failed
    // preview never yanks away the terms the subscriber came here to look at.
    expect(screen.getByText(/Screenings/)).toBeInTheDocument();
  });
});
