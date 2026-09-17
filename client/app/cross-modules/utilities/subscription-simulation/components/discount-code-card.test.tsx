import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import type {
  DiscountCodePreview,
  SubscriptionPurchasePreview,
} from "../models/subscription-simulation.model";

const previewDiscountCode = vi.fn();

vi.mock("../services/subscription-simulation.service", async () => {
  const actual = await vi.importActual<
    typeof import("../services/subscription-simulation.service")
  >("../services/subscription-simulation.service");

  return {
    ...actual,
    subscriptionSimulationService: {
      previewDiscountCode: (...args: unknown[]) => previewDiscountCode(...args),
    },
  };
});

import { DiscountCodeCard } from "./discount-code-card";

const plan = {
  planId: "plan-1",
  code: "professional",
  displayName: "Professional",
  quantityItems: [
    { itemKey: "seat", unitLabel: "seat", minQuantity: 1, maxQuantity: null, defaultQuantity: 3 },
  ],
  prices: [
    {
      priceId: "price-1",
      currencyCode: "CHF",
      unitAmountMinor: 8_900,
      interval: "Month",
      intervalCount: 1,
      quantityItemKey: "seat",
      displayPriceNote: null,
    },
  ],
} as unknown as SubscriptionPlan;

const quote: SubscriptionPurchasePreview = {
  currencyCode: "CHF",
  subtotalMinor: 8_900,
  discountMinor: 2_000,
  builtInDiscountMinor: 0,
  promotionalDiscountMinor: 2_000,
  taxMinor: 0,
  netSubtotalMinor: 6_900,
  tax: null,
  totalDueNowMinor: 6_900,
  prorated: false,
  coveredDays: null,
  totalDays: null,
  periodStartUtc: "2026-08-16T00:00:00Z",
  periodEndUtc: "2026-09-16T00:00:00Z",
  nextRenewalAtUtc: "2026-09-16T00:00:00Z",
  nextRenewalAmountMinor: 8_900,
  nextRenewal: {
    subtotalMinor: 8_900,
    builtInDiscountMinor: 0,
    promotionalDiscountMinor: 0,
    discountMinor: 0,
    netSubtotalMinor: 8_900,
    tax: null,
    totalMinor: 8_900,
    renewalAtUtc: "2026-09-16T00:00:00Z",
  },
  nextCharge: {
    chargeAtUtc: "2026-09-16T00:00:00Z",
    periodStartUtc: "2026-09-16T00:00:00Z",
    periodEndUtc: "2026-10-16T00:00:00Z",
    prorated: false,
    coveredDays: null,
    totalDays: null,
    subtotalMinor: 8_900,
    builtInDiscountMinor: 0,
    promotionalDiscountMinor: 0,
    discountMinor: 0,
    netSubtotalMinor: 8_900,
    tax: null,
    totalMinor: 8_900,
  },
  trialEndsAtUtc: null,
  requiresCardSetup: false,
  pendingAnnualPeriod: null,
  campaign: null,
  blockers: [],
  quotedAtUtc: "2026-08-16T00:00:00Z",
  quoteValidUntilUtc: null,
};

const applied: DiscountCodePreview = { status: "Applied", reasonCode: null, message: null, quote };

const expired: DiscountCodePreview = {
  status: "Expired",
  reasonCode: "subscription_discount_expired",
  message: "This code has expired.",
  quote: {
    ...quote,
    discountMinor: 0,
    promotionalDiscountMinor: 0,
    netSubtotalMinor: 8_900,
    totalDueNowMinor: 8_900,
  },
};

const renderCard = () =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <DiscountCodeCard plans={[plan]} organizationId="org-1" />
    </QueryClientProvider>,
  );

const typeCode = (value: string) =>
  fireEvent.change(screen.getByLabelText("Code"), { target: { value } });

describe("DiscountCodeCard", () => {
  beforeEach(() => {
    previewDiscountCode.mockReset();
  });

  it("refuses to call the server without a code", () => {
    renderCard();

    fireEvent.click(screen.getByRole("button", { name: "Test code" }));

    expect(previewDiscountCode).not.toHaveBeenCalled();
    expect(screen.getByText("Enter a discount code.")).toBeInTheDocument();
  });

  it("sends the plan, price, code and the plan's default quantities", async () => {
    previewDiscountCode.mockResolvedValue(applied);
    renderCard();

    typeCode("  launch20  ");
    fireEvent.click(screen.getByRole("button", { name: "Test code" }));

    await waitFor(() => expect(previewDiscountCode).toHaveBeenCalledTimes(1));
    expect(previewDiscountCode.mock.calls[0][0]).toMatchObject({
      planCode: "professional",
      priceId: "price-1",
      discountCode: "launch20",
      quantities: [{ itemKey: "seat", quantity: 3 }],
      organizationId: "org-1",
    });
  });

  it("shows what an applied code is worth", async () => {
    previewDiscountCode.mockResolvedValue(applied);
    renderCard();

    typeCode("LAUNCH20");
    fireEvent.click(screen.getByRole("button", { name: "Test code" }));

    expect(await screen.findByTestId("discount-verdict")).toBeInTheDocument();
    expect(screen.getByText("Applied")).toBeInTheDocument();
    expect(screen.getByText(/Worth/)).toBeInTheDocument();
  });

  it("reports a rejection as the answer, with its reason code", async () => {
    previewDiscountCode.mockResolvedValue(expired);
    renderCard();

    typeCode("OLDCODE");
    fireEvent.click(screen.getByRole("button", { name: "Test code" }));

    expect(await screen.findByText("Expired")).toBeInTheDocument();
    expect(screen.getByText("This code has expired.")).toBeInTheDocument();
    expect(screen.getByText("subscription_discount_expired")).toBeInTheDocument();
  });

  it("discards the verdict once the code is edited", async () => {
    previewDiscountCode.mockResolvedValue(applied);
    renderCard();

    typeCode("LAUNCH20");
    fireEvent.click(screen.getByRole("button", { name: "Test code" }));
    expect(await screen.findByTestId("discount-verdict")).toBeInTheDocument();

    typeCode("LAUNCH21");

    expect(screen.queryByTestId("discount-verdict")).not.toBeInTheDocument();
  });

  it("surfaces a failed check without a verdict", async () => {
    previewDiscountCode.mockRejectedValue(new Error("The plan could not be found."));
    renderCard();

    typeCode("LAUNCH20");
    fireEvent.click(screen.getByRole("button", { name: "Test code" }));

    expect(await screen.findByText("The plan could not be found.")).toBeInTheDocument();
    expect(screen.queryByTestId("discount-verdict")).not.toBeInTheDocument();
  });

  it("shows the charge actually taken next when the code is spent by then", async () => {
    previewDiscountCode.mockResolvedValue({
      ...applied,
      quote: {
        ...quote,
        nextRenewal: { ...quote.nextRenewal, promotionalDiscountMinor: 2_000, totalMinor: 6_900 },
        nextCharge: { ...quote.nextCharge, promotionalDiscountMinor: 0, totalMinor: 8_900 },
      },
    });
    renderCard();

    typeCode("LAUNCH20");
    fireEvent.click(screen.getByRole("button", { name: "Test code" }));

    expect(await screen.findByTestId("discount-next-charge")).toBeInTheDocument();
    const panel = screen.getByTestId("discount-verdict");
    expect(panel.textContent).toContain("Charged next");
    expect(panel.textContent).toContain("89.00");
    expect(panel.textContent).toContain("Recurring price");
    expect(panel.textContent).not.toContain("Next renewal");
  });

  it("shows one figure when the code survives to the next renewal", async () => {
    previewDiscountCode.mockResolvedValue({
      ...applied,
      quote: {
        ...quote,
        nextRenewal: { ...quote.nextRenewal, promotionalDiscountMinor: 2_000, totalMinor: 6_900 },
        nextCharge: { ...quote.nextCharge, promotionalDiscountMinor: 2_000, totalMinor: 6_900 },
      },
    });
    renderCard();

    typeCode("LAUNCH20");
    fireEvent.click(screen.getByRole("button", { name: "Test code" }));

    await screen.findByTestId("discount-verdict");
    expect(screen.queryByTestId("discount-next-charge")).not.toBeInTheDocument();
    expect(screen.getByTestId("discount-verdict").textContent).toContain("Next renewal");
  });
});
