import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import type {
  QuantityChangeQuote,
  SimulatedSubscription,
} from "../models/subscription-simulation.model";

const toast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({ toast: (...args: unknown[]) => toast(...args) }));

const previewQuantityChange = vi.fn();
const changeQuantity = vi.fn();

vi.mock("../services/subscription-simulation.service", async () => {
  const actual = await vi.importActual<
    typeof import("../services/subscription-simulation.service")
  >("../services/subscription-simulation.service");

  return {
    ...actual,
    subscriptionSimulationService: {
      previewQuantityChange: (...args: unknown[]) => previewQuantityChange(...args),
      changeQuantity: (...args: unknown[]) => changeQuantity(...args),
    },
  };
});

import { ChangeQuantityDialog } from "./change-quantity-dialog";
import { SubscriptionOperationError } from "../services/subscription-simulation.service";

const subscription: SimulatedSubscription = {
  subscriptionId: "sub-1",
  status: "Active",
  planCode: "team",
  planName: "Team",
  currencyCode: "CHF",
  unitAmountMinor: 14_500,
  interval: "Month",
  intervalCount: 1,
  displayPriceNote: null,
  quantities: [{ itemKey: "user", quantity: 4, unitLabel: "user" }],
  currentPeriodStartUtc: "2026-08-01T00:00:00Z",
  currentPeriodEndUtc: "2026-09-01T00:00:00Z",
  nextPaymentAtUtc: "2026-09-01T00:00:00Z",
  trialEndsAtUtc: null,
  cancelAtPeriodEnd: false,
  canceledAtUtc: null,
  pendingQuantityChange: null,
  currentTier: { minimumQuantity: 1, maximumQuantity: 4, discountBasisPoints: 0 },
  recurringAmountMinor: 58_000,
  checkoutUrl: null,
  meters: [],
  version: 7,
};

const plan = {
  planId: "plan-1",
  quantityItems: [
    { itemKey: "user", unitLabel: "user", minQuantity: 1, maxQuantity: null, defaultQuantity: 1 },
  ],
} as unknown as SubscriptionPlan;

const increaseQuote: QuantityChangeQuote = {
  subscriptionId: "sub-1",
  version: 7,
  preview: true,
  timing: "Immediate",
  effectiveAtUtc: "2026-08-16T12:00:00Z",
  quantities: [{ itemKey: "user", quantity: 5, unitLabel: "user" }],
  currentTier: { minimumQuantity: 1, maximumQuantity: 4, discountBasisPoints: 0 },
  targetTier: { minimumQuantity: 5, maximumQuantity: 9, discountBasisPoints: 500 },
  proratedChargeMinor: 5_437,
  nextRenewalAmountMinor: 68_875,
  effectiveUnitAmountMinor: 13_775,
  taxAmountMinor: 0,
  promotionApplied: false,
  currencyCode: "CHF",
  chargePaymentDetailId: null,
  pendingQuantityChange: null,
};

const decreaseQuote: QuantityChangeQuote = {
  ...increaseQuote,
  timing: "NextPeriod",
  effectiveAtUtc: "2026-09-01T00:00:00Z",
  quantities: [{ itemKey: "user", quantity: 3, unitLabel: "user" }],
  targetTier: { minimumQuantity: 1, maximumQuantity: 4, discountBasisPoints: 0 },
  proratedChargeMinor: 0,
  nextRenewalAmountMinor: 43_500,
  effectiveUnitAmountMinor: 14_500,
  taxAmountMinor: 0,
  promotionApplied: false,
  pendingQuantityChange: {
    quantities: [{ itemKey: "user", quantity: 3, unitLabel: "user" }],
    requestedAtUtc: "2026-08-16T12:00:00Z",
    effectiveAtUtc: "2026-09-01T00:00:00Z",
  },
};

const onRefresh = vi.fn();

const renderDialog = (current: SimulatedSubscription = subscription) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={client}>
      <ChangeQuantityDialog
        subscription={current}
        currentPlan={plan}
        organizationId="org-acting-as"
        open
        onOpenChange={() => {}}
        onRefresh={onRefresh}
      />
    </QueryClientProvider>,
  );
};

const setQuantity = (value: string) =>
  fireEvent.change(screen.getByLabelText(/users/), { target: { value } });

const click = (name: RegExp) => fireEvent.click(screen.getByRole("button", { name }));

beforeEach(() => {
  vi.clearAllMocks();
});

describe("ChangeQuantityDialog", () => {
  it("previews the target band and its prorated charge before anything is confirmed", async () => {
    previewQuantityChange.mockResolvedValue(increaseQuote);

    renderDialog();
    setQuantity("5");
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByText("5–9 · 5% off")).toBeInTheDocument();
    });

    // CHF 54.37 now, CHF 137.75 per user at the new band, CHF 688.75 next renewal.
    expect(screen.getByText(/54\.37/)).toBeInTheDocument();
    expect(screen.getByText(/137\.75 each, before tax/)).toBeInTheDocument();
    expect(screen.getByText(/688\.75/)).toBeInTheDocument();

    // The preview writes nothing.
    expect(changeQuantity).not.toHaveBeenCalled();
  });

  it("sends the quantity and the version it was quoted against, never a band", async () => {
    previewQuantityChange.mockResolvedValue(increaseQuote);

    renderDialog();
    setQuantity("5");
    click(/^Preview$/);

    await waitFor(() => expect(previewQuantityChange).toHaveBeenCalled());

    expect(previewQuantityChange).toHaveBeenCalledWith("sub-1", {
      version: 7,
      quantities: [{ itemKey: "user", quantity: 5 }],
      organizationId: "org-acting-as",
    });
  });

  it("cannot confirm before a preview", () => {
    renderDialog();
    setQuantity("5");

    expect(screen.getByRole("button", { name: /Confirm and pay/ })).toBeDisabled();
  });

  it("discards the quote when the quantity is edited again", async () => {
    previewQuantityChange.mockResolvedValue(increaseQuote);

    renderDialog();
    setQuantity("5");
    click(/^Preview$/);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Confirm and pay/ })).not.toBeDisabled(),
    );

    // Confirming figures the subscriber can no longer see is confirming numbers they never saw.
    setQuantity("9");

    expect(screen.getByRole("button", { name: /Confirm and pay/ })).toBeDisabled();
  });

  it("applies a confirmed increase and reports it", async () => {
    previewQuantityChange.mockResolvedValue(increaseQuote);
    changeQuantity.mockResolvedValue({ ...increaseQuote, preview: false, version: 9 });

    renderDialog();
    setQuantity("5");
    click(/^Preview$/);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Confirm and pay/ })).not.toBeDisabled(),
    );

    click(/Confirm and pay/);

    await waitFor(() => expect(changeQuantity).toHaveBeenCalled());

    expect(changeQuantity).toHaveBeenCalledWith("sub-1", {
      version: 7,
      quantities: [{ itemKey: "user", quantity: 5 }],
      organizationId: "org-acting-as",
    });
    expect(toast).toHaveBeenCalledWith(
      expect.objectContaining({ variant: "success", title: "Quantity updated" }),
    );
  });

  it("shows the unit price the server stated rather than one derived here", async () => {
    // A promotion, the plan's combination policy and the server's own rounding all move this, so a
    // percentage applied to the list price in the browser can disagree with the charge being
    // confirmed — on the one screen where that matters most.
    previewQuantityChange.mockResolvedValue({
      ...increaseQuote,
      // Deliberately not 145 less 5%: a client recomputing from the band would print 137.75.
      effectiveUnitAmountMinor: 11_020,
      promotionApplied: true,
    });

    renderDialog();
    setQuantity("5");
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByText(/110\.20 each/)).toBeInTheDocument();
    });

    expect(screen.queryByText(/137\.75/)).not.toBeInTheDocument();
    expect(screen.getByText(/Includes the discount on this subscription/)).toBeInTheDocument();
  });

  it("states a unit price even where the quantity selects no band", async () => {
    previewQuantityChange.mockResolvedValue({
      ...increaseQuote,
      targetTier: null,
      effectiveUnitAmountMinor: 14_500,
    });

    renderDialog();
    setQuantity("5");
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByText(/145\.00 each/)).toBeInTheDocument();
    });
  });

  it("states tax beside the total rather than inside the unit price", async () => {
    // Folded in, a 5% band on a taxed price showed a unit costing more than the list price on a
    // card that also said 5% off.
    previewQuantityChange.mockResolvedValue({
      ...increaseQuote,
      taxAmountMinor: 5_510,
      nextRenewalAmountMinor: 74_385,
    });

    renderDialog();
    setQuantity("5");
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByText("Next renewal, incl. tax")).toBeInTheDocument();
    });

    expect(screen.getByText(/137\.75 each, before tax/)).toBeInTheDocument();
    expect(screen.getByText("of which tax")).toBeInTheDocument();
    expect(screen.getByText(/55\.10/)).toBeInTheDocument();
  });

  it("says nothing about a unit price on a flat fee", async () => {
    // A plan that tracks a quantity item for free has no per-unit price, and printing the whole
    // plan fee as the cost of "each" would be worse than printing nothing.
    previewQuantityChange.mockResolvedValue({
      ...increaseQuote,
      effectiveUnitAmountMinor: null,
    });

    renderDialog();
    setQuantity("5");
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByText("Next renewal")).toBeInTheDocument();
    });

    expect(screen.queryByText(/each/)).not.toBeInTheDocument();
    expect(screen.queryByText("Effective unit price")).not.toBeInTheDocument();
  });

  it("explains a flat-fee promotion without inventing a unit price or a band", async () => {
    // The discount is real and worth saying; the unit price and the band are not, and the note
    // used to describe both on a card that showed neither.
    previewQuantityChange.mockResolvedValue({
      ...increaseQuote,
      effectiveUnitAmountMinor: null,
      targetTier: null,
      promotionApplied: true,
    });

    renderDialog();
    setQuantity("5");
    click(/^Preview$/);

    await waitFor(() => {
      expect(
        screen.getByText("Includes the promotional discount on this subscription."),
      ).toBeInTheDocument();
    });

    // The row that says there is no band is fine — it is true, and it is not the note under test.
    expect(screen.queryByText(/unit price is below/)).not.toBeInTheDocument();
    expect(screen.queryByText(/each/)).not.toBeInTheDocument();
    expect(screen.getByText("No band — one price at every quantity")).toBeInTheDocument();
  });

  it("says a decrease waits for the paid period and names the date", async () => {
    previewQuantityChange.mockResolvedValue(decreaseQuote);

    renderDialog();
    setQuantity("3");
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByText("Scheduled for the period end")).toBeInTheDocument();
    });

    expect(screen.getByText(/Nothing is refunded/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Schedule change/ })).toBeInTheDocument();
    // Never "charged now" on a reduction.
    expect(screen.getByText("Nothing")).toBeInTheDocument();
  });

  it("shows a scheduled reduction that is already booked", () => {
    renderDialog({
      ...subscription,
      pendingQuantityChange: decreaseQuote.pendingQuantityChange,
    });

    expect(screen.getByText(/is already scheduled for/)).toBeInTheDocument();
  });

  it("reloads and asks for a fresh preview after a stale version", async () => {
    previewQuantityChange.mockResolvedValue(increaseQuote);
    changeQuantity.mockRejectedValue(
      new SubscriptionOperationError("Conflict.", "subscription_version_conflict", 409),
    );

    renderDialog();
    setQuantity("5");
    click(/^Preview$/);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Confirm and pay/ })).not.toBeDisabled(),
    );

    click(/Confirm and pay/);

    await waitFor(() => {
      expect(screen.getByText(/preview the new quantity again/)).toBeInTheDocument();
    });

    // Re-read, and the stale quote withdrawn so it cannot be sent a second time.
    expect(onRefresh).toHaveBeenCalled();
    expect(screen.getByRole("button", { name: /Confirm and pay/ })).toBeDisabled();
  });

  it("keeps the quantity as it was when the card is declined", async () => {
    previewQuantityChange.mockResolvedValue(increaseQuote);
    changeQuantity.mockRejectedValue(
      new SubscriptionOperationError("Declined.", "subscription_quantity_charge_failed", 422),
    );

    renderDialog();
    setQuantity("5");
    click(/^Preview$/);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Confirm and pay/ })).not.toBeDisabled(),
    );

    click(/Confirm and pay/);

    await waitFor(() => {
      expect(screen.getByText(/Nothing changed/)).toBeInTheDocument();
    });

    // A decline is not an application: the dialog stays open and reports it, and nothing claims
    // the larger quantity is in force.
    expect(toast).not.toHaveBeenCalledWith(
      expect.objectContaining({ variant: "success" }),
    );
  });

  it("tells a subscriber not to retry a charge nobody can answer for", async () => {
    previewQuantityChange.mockResolvedValue(increaseQuote);
    changeQuantity.mockRejectedValue(
      new SubscriptionOperationError(
        "Unresolved.",
        "subscription_quantity_charge_unresolved",
        504,
      ),
    );

    renderDialog();
    setQuantity("5");
    click(/^Preview$/);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Confirm and pay/ })).not.toBeDisabled(),
    );

    click(/Confirm and pay/);

    await waitFor(() => {
      expect(screen.getByText(/Do not try again/)).toBeInTheDocument();
    });

    // The money may already have moved, so this is the one failure that must not invite a retry.
    expect(screen.getByRole("button", { name: /Confirm and pay/ })).toBeDisabled();
    expect(onRefresh).not.toHaveBeenCalled();
  });

  it("explains a settlement that is still in flight", async () => {
    previewQuantityChange.mockRejectedValue(
      new SubscriptionOperationError(
        "In flight.",
        "subscription_quantity_change_in_flight",
        409,
      ),
    );

    renderDialog();
    setQuantity("5");
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByText(/still being settled/)).toBeInTheDocument();
    });
  });

  it("refuses a quantity outside the plan before calling the server", () => {
    renderDialog();
    setQuantity("0");
    click(/^Preview$/);

    expect(screen.getByText(/at least 1 user/)).toBeInTheDocument();
    expect(previewQuantityChange).not.toHaveBeenCalled();
  });

  it("refuses a quantity that is already in force", () => {
    renderDialog();
    setQuantity("4");
    click(/^Preview$/);

    expect(screen.getByText(/already the quantity in force/)).toBeInTheDocument();
    expect(previewQuantityChange).not.toHaveBeenCalled();
  });
});
