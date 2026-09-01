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
  pendingPlanChange: null,
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
  timing: "Immediate",
  effectiveAtUtc: "2026-08-15T00:00:00Z",
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

  /**
   * The static copy above the quote used to promise "takes effect immediately" and "a downgrade
   * becomes credit toward future renewals" unconditionally — both false since scheduled changes
   * (a downgrade, or an annual cadence change) neither apply today nor bank anything. Guards
   * against that copy coming back rather than pinning down its exact replacement wording, which is
   * free to keep improving.
   */
  it("never tells every change it applies immediately or banks credit as a downgrade", () => {
    const { container } = renderDialog();

    expect(container.textContent).not.toMatch(/takes effect immediately/i);
    expect(container.textContent).not.toMatch(/downgrade becomes credit/i);
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

  /**
   * A prepaid opening stub's compatible upgrade settles the stub and the paid year together.
   * The dialog has to split that back apart for display — the server reports only the stub's
   * own sides at the top level plus a combined post-credit total, never the stub's own raw delta
   * directly — so this pins down the arithmetic rather than trusting a plausible-looking
   * subtraction.
   */
  it("shows the stub and the prepaid year as two separate adjustments", async () => {
    previewPlanChange.mockResolvedValue({
      ...quote,
      chargeMinor: 90_000,
      settlement: {
        // The stub's own sides, pre-credit: 10,000 owed for the remaining days.
        outgoing: {
          grossAmountMinor: 14_500,
          builtInDiscountMinor: 0,
          promotionalDiscountMinor: 0,
          taxAmountMinor: 0,
          periodTotalMinor: 14_500,
          proratedValueMinor: 20_000,
        },
        target: {
          grossAmountMinor: 18_000,
          builtInDiscountMinor: 0,
          promotionalDiscountMinor: 0,
          taxAmountMinor: 0,
          periodTotalMinor: 18_000,
          proratedValueMinor: 30_000,
        },
        // Combined, post-credit: 10,000 (stub) + 100,000 (year) - 20,000 (credit) = 90,000.
        creditConsumedMinor: 20_000,
        netSettlementMinor: 90_000,
        annual: {
          outgoing: {
            grossAmountMinor: 1_000_000,
            builtInDiscountMinor: 0,
            promotionalDiscountMinor: 0,
            taxAmountMinor: 0,
            periodTotalMinor: 1_000_000,
            proratedValueMinor: 1_000_000,
          },
          target: {
            grossAmountMinor: 1_100_000,
            builtInDiscountMinor: 0,
            promotionalDiscountMinor: 0,
            taxAmountMinor: 0,
            periodTotalMinor: 1_100_000,
            proratedValueMinor: 1_100_000,
          },
          // The annual side's own raw delta — reported for display, no credit of its own.
          creditConsumedMinor: 0,
          netSettlementMinor: 100_000,
        },
      },
    });

    renderDialog();
    selectTargetPlan();
    click(/^Preview$/);

    const dialog = await screen.findByTestId("plan-change-quote");

    // 30,000 - 20,000 = 10,000, the stub's own raw delta — not the combined total, and not the
    // combined total minus the annual delta either (which credit would corrupt).
    expect(dialog.textContent).toContain("Opening stub adjustment");
    expect(dialog.textContent).toContain("100.00"); // stub: CHF 100.00 = 10,000 minor
    expect(dialog.textContent).toContain("Prepaid annual-period adjustment");
    expect(dialog.textContent).toContain("1,000.00"); // annual: CHF 1,000.00 = 100,000 minor
    expect(dialog.textContent).toContain("Net settlement");
    expect(dialog.textContent).toContain("900.00"); // combined post-credit: CHF 900.00 = 90,000
    expect(dialog.textContent).toContain("Paid from your credit");
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

describe("ChangePlanDialog timing", () => {
  const scheduledQuote: SubscriptionPlanChangePreview = {
    ...quote,
    chargeMinor: 0,
    timing: "NextRenewal",
    effectiveAtUtc: "2026-10-01T00:00:00Z",
  };

  /**
   * The figure alone is ambiguous: "nothing due" reads the same for a change that costs nothing
   * today and one that costs nothing ever. The date is what tells the subscriber which.
   */
  it("says a scheduled change takes effect later and charges nothing today", async () => {
    previewPlanChange.mockResolvedValue(scheduledQuote);

    renderDialog();
    selectTargetPlan();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("plan-change-quote")).toBeInTheDocument();
    });

    expect(screen.getByText(/Nothing due today/i)).toBeInTheDocument();
    expect(screen.getByTestId("plan-change-scheduled-note")).toHaveTextContent(
      /keep your current plan/i,
    );
  });

  it("still says an immediate upgrade is charged now", async () => {
    previewPlanChange.mockResolvedValue(quote);

    renderDialog();
    selectTargetPlan();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("plan-change-quote")).toBeInTheDocument();
    });

    expect(screen.getByText(/Charged now/i)).toBeInTheDocument();
    expect(screen.queryByTestId("plan-change-scheduled-note")).not.toBeInTheDocument();
  });

  /**
   * Confirming a scheduled change must not be announced as though the plan had moved.
   */
  it("announces a booking rather than a completed change", async () => {
    previewPlanChange.mockResolvedValue(scheduledQuote);
    changePlan.mockResolvedValue({});

    renderDialog();
    selectTargetPlan();
    click(/^Preview$/);

    await waitFor(() => screen.getByTestId("plan-change-quote"));

    click(/^Confirm change$/);

    await waitFor(() => expect(toast).toHaveBeenCalled());

    expect(toast).toHaveBeenCalledWith(
      expect.objectContaining({ title: "Plan change scheduled" }),
    );
  });

  /**
   * A cross-blocking 409 names the thing to go and do, rather than repeating a code.
   */
  it("explains a refusal caused by an already-scheduled quantity change", async () => {
    previewPlanChange.mockResolvedValue(quote);
    changePlan.mockRejectedValue(
      Object.assign(new Error("Conflict"), {
        code: "subscription_pending_quantity_change_exists",
      }),
    );

    renderDialog();
    selectTargetPlan();
    click(/^Preview$/);

    await waitFor(() => screen.getByTestId("plan-change-quote"));

    click(/^Confirm change$/);

    await waitFor(() => {
      expect(screen.getByText(/only one change can be waiting at a time/i)).toBeInTheDocument();
    });
  });
});
