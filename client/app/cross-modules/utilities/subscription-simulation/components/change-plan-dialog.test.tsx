import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import type {
  SimulatedSubscription,
  SubscriptionPlanChangePreview,
} from "../models/subscription-simulation.model";

const toast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({ toast: (...args: unknown[]) => toast(...args) }));

const previewPlanChange = vi.fn();
const changePlan = vi.fn();

vi.mock("../services/subscription-simulation.service", async () => {
  const actual = await vi.importActual<
    typeof import("../services/subscription-simulation.service")
  >("../services/subscription-simulation.service");

  return {
    ...actual,
    subscriptionSimulationService: {
      previewPlanChange: (...args: unknown[]) => previewPlanChange(...args),
      changePlan: (...args: unknown[]) => changePlan(...args),
    },
  };
});

import { ChangePlanDialog } from "./change-plan-dialog";

const currentPlan = {
  planId: "plan-basic",
  code: "basic",
  displayName: "Basic",
  quantityItems: [],
  prices: [
    {
      priceId: "price-basic",
      currencyCode: "CHF",
      unitAmountMinor: 10_000,
      interval: "Month",
      intervalCount: 1,
      quantityItemKey: null,
      displayPriceNote: null,
    },
  ],
} as unknown as SubscriptionPlan;

const premiumPlan = {
  planId: "plan-premium",
  code: "premium",
  displayName: "Premium",
  quantityItems: [],
  prices: [
    {
      priceId: "price-premium",
      currencyCode: "CHF",
      unitAmountMinor: 20_000,
      interval: "Month",
      intervalCount: 1,
      quantityItemKey: null,
      displayPriceNote: null,
    },
    {
      priceId: "price-premium-annual",
      currencyCode: "CHF",
      unitAmountMinor: 200_000,
      interval: "Year",
      intervalCount: 1,
      quantityItemKey: null,
      displayPriceNote: null,
    },
  ],
} as unknown as SubscriptionPlan;

const subscription: SimulatedSubscription = {
  subscriptionId: "sub-1",
  status: "Active",
  planCode: "basic",
  planName: "Basic",
  currencyCode: "CHF",
  unitAmountMinor: 10_000,
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
  recurringAmountMinor: 10_000,
  checkoutUrl: null,
  meters: [],
  version: 1,
};

const quote: SubscriptionPlanChangePreview = {
  currencyCode: "CHF",
  targetPlanCode: "premium",
  targetPlanName: "Premium",
  targetPriceId: "price-premium",
  interval: "Month",
  intervalCount: 1,
  quantities: [],
  chargeMinor: 5_000,
  creditBankedMinor: 0,
  settlement: {
    outgoing: {
      grossAmountMinor: 10_000,
      builtInDiscountMinor: 0,
      promotionalDiscountMinor: 0,
      taxAmountMinor: 0,
      periodTotalMinor: 10_000,
      proratedValueMinor: 5_000,
    },
    target: {
      grossAmountMinor: 20_000,
      builtInDiscountMinor: 0,
      promotionalDiscountMinor: 0,
      taxAmountMinor: 0,
      periodTotalMinor: 20_000,
      proratedValueMinor: 10_000,
    },
    creditConsumedMinor: 0,
    netSettlementMinor: 5_000,
  },
  newPeriodStartUtc: "2026-08-01T00:00:00Z",
  newPeriodEndUtc: "2026-09-01T00:00:00Z",
  nextRenewalAmountMinor: 20_000,
  blockers: [],
  quotedAtUtc: "2026-08-16T00:00:00Z",
};

const renderDialog = () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={client}>
      <ChangePlanDialog
        subscription={subscription}
        currentPlan={currentPlan}
        plans={[currentPlan, premiumPlan]}
        organizationId="org-1"
        open
        onOpenChange={() => {}}
      />
    </QueryClientProvider>,
  );
};

const click = (name: RegExp) => fireEvent.click(screen.getByRole("button", { name }));

const selectTargetPlan = () => {
  const target = screen.getByRole("combobox", { name: /Target plan/ });
  fireEvent.click(target);
  fireEvent.click(screen.getByText("Premium"));
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe("ChangePlanDialog", () => {
  it("cannot be confirmed before a preview is taken", () => {
    renderDialog();

    expect(screen.getByRole("button", { name: /^Confirm change$/ })).toBeDisabled();
  });

  it("previews the settlement before enabling confirm", async () => {
    previewPlanChange.mockResolvedValue(quote);

    renderDialog();
    selectTargetPlan();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("plan-change-quote")).toBeInTheDocument();
    });

    expect(screen.getByTestId("plan-change-quote").textContent).toContain("50.00");
    expect(screen.getByRole("button", { name: /^Confirm change$/ })).toBeEnabled();
    expect(changePlan).not.toHaveBeenCalled();
  });

  it("discards the quote when the target price is edited after previewing", async () => {
    previewPlanChange.mockResolvedValue(quote);

    renderDialog();
    selectTargetPlan();
    click(/^Preview$/);

    await waitFor(() => screen.getByTestId("plan-change-quote"));

    const priceSelect = screen.getByRole("combobox", { name: /Target price/ });
    fireEvent.click(priceSelect);
    fireEvent.click(screen.getByText(/every year/));

    expect(screen.queryByTestId("plan-change-quote")).not.toBeInTheDocument();
  });

  it("sends exactly what was previewed when confirmed", async () => {
    previewPlanChange.mockResolvedValue(quote);
    changePlan.mockResolvedValue({ ...subscription, planCode: "premium" });

    renderDialog();
    selectTargetPlan();
    click(/^Preview$/);

    await waitFor(() => screen.getByTestId("plan-change-quote"));

    click(/^Confirm change$/);

    await waitFor(() => {
      expect(changePlan).toHaveBeenCalledWith(
        "sub-1",
        expect.objectContaining({
          planCode: "premium",
          priceId: "price-premium",
          organizationId: "org-1",
        }),
      );
    });
  });

  it("shows a blocker and keeps confirm disabled even though the price is quoted", async () => {
    previewPlanChange.mockResolvedValue({
      ...quote,
      blockers: [
        {
          code: "subscription_plan_change_no_payment_method",
          message: "This upgrade cannot be charged without a saved payment method.",
        },
      ],
    });

    renderDialog();
    selectTargetPlan();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByText(/saved payment method/)).toBeInTheDocument();
    });

    expect(screen.getByRole("button", { name: /^Confirm change$/ })).toBeDisabled();
    expect(changePlan).not.toHaveBeenCalled();
  });
});
