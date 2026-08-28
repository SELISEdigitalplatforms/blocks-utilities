import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { SubscriptionPlan } from "../models/subscription-plan.model";
import { PlanPricesEditor } from "./plan-prices-editor";

const plan = (overrides: Partial<SubscriptionPlan> = {}): SubscriptionPlan => ({
  planId: "plan-1",
  code: "professional",
  displayName: "Professional",
  description: null,
  featuresJson: null,
  organizationId: "org-1",
  trialDays: null,
  trialRequiresPaymentMethod: true,
  version: 3,
  // The whole point of this component: the plan is being sold on already.
  hasSubscribers: true,
  quantityItems: [],
  meters: [],
  entitlements: [],
  prices: [
    {
      priceId: "price-live",
      currencyCode: "CHF",
      unitAmountMinor: 2_500,
      interval: "Month",
      intervalCount: 1,
      quantityItemKey: null,
    },
  ],
  trialGrants: [],
  ...overrides,
});

/** The header links back to the plan, so a router is not optional. */
const renderInRouter = (ui: ReactNode) => render(<MemoryRouter>{ui}</MemoryRouter>);

const renderEditor = (overrides: Partial<SubscriptionPlan> = {}) => {
  const onSubmit = vi.fn().mockResolvedValue(undefined);
  const onRetirePrice = vi.fn();

  renderInRouter(
    <PlanPricesEditor
      plan={plan(overrides)}
      backTo="/app/item-1/subscription/plans/plan-1"
      isSubmitting={false}
      onRetirePrice={onRetirePrice}
      onUpdatePriceTax={vi.fn().mockResolvedValue(undefined)}
      onUpdatePriceDiscount={vi.fn().mockResolvedValue(undefined)}
      onSubmit={onSubmit}
    />,
  );

  return { onSubmit, onRetirePrice };
};

/**
 * The regression this guards is a dead end, not a broken form.
 *
 * A subscribed plan used to reach a card that said its terms could no longer change and offered
 * nothing further, while the only other way to add a price was a separate page that has since been
 * deleted. Between them, a live plan could not be repriced at all — and repricing is the one thing
 * only a live plan needs, since a plan nobody is on can simply be edited.
 */
describe("PlanPricesEditor", () => {
  it("lets a subscribed plan be priced instead of turning it away", () => {
    renderEditor();

    expect(
      screen.getByRole("heading", { name: "Prices for Professional" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Add prices/ })).toBeEnabled();
  });

  it("says which half is closed, so nobody looks for the name field", () => {
    renderEditor();

    expect(screen.getByText(/own terms are settled/i)).toBeInTheDocument();
    // The instruction that matters, because it is the only route to a new amount.
    expect(screen.getByText(/add the new price and retire the old one/i)).toBeInTheDocument();
  });

  it("shows what the plan is already sold on, and can retire it", async () => {
    const { onRetirePrice } = renderEditor();

    expect(screen.getByText("Already on this plan")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Retire" }));

    expect(onRetirePrice).toHaveBeenCalledWith("price-live");
  });

  it("refuses to submit with no price authored, rather than posting nothing", async () => {
    const { onSubmit } = renderEditor();

    await userEvent.click(screen.getByRole("button", { name: /Add prices/ }));

    await waitFor(() => {
      expect(
        screen.getByText(/Add at least one price/i),
      ).toBeInTheDocument();
    });
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("submits the authored price and nothing about the plan's own terms", async () => {
    const { onSubmit } = renderEditor();

    await userEvent.click(screen.getByRole("button", { name: "Add another price" }));
    await userEvent.clear(screen.getByPlaceholderText("89.00"));
    await userEvent.type(screen.getByPlaceholderText("89.00"), "49");

    await userEvent.click(screen.getByRole("button", { name: /Add prices/ }));

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledTimes(1);
    });

    const values = onSubmit.mock.calls[0][0];

    expect(values.prices).toHaveLength(1);
    expect(values.prices[0].amount).toBe(49);
    // The caller sends only the prices; the plan's own values ride along as form state because the
    // price fields need the quantity items, and are never posted.
    expect(values.displayName).toBe("Professional");
  });

  it("surfaces a rejected price in the form rather than losing what was typed", async () => {
    renderInRouter(
      <PlanPricesEditor
        plan={plan()}
        backTo="/app/item-1/subscription/plans/plan-1"
        isSubmitting={false}
        onRetirePrice={vi.fn()}
        onUpdatePriceTax={vi.fn().mockResolvedValue(undefined)}
        onUpdatePriceDiscount={vi.fn().mockResolvedValue(undefined)}
        onSubmit={vi.fn().mockRejectedValue(new Error("Another price already charges on these terms."))}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: "Add another price" }));
    await userEvent.clear(screen.getByPlaceholderText("89.00"));
    await userEvent.type(screen.getByPlaceholderText("89.00"), "25");

    await userEvent.click(screen.getByRole("button", { name: /Add prices/ }));

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent(
        "Another price already charges on these terms.",
      );
    });

    // Still there. A price that failed is a message to read, not a form to fill in again.
    expect(screen.getByPlaceholderText("89.00")).toHaveValue(25);
  });
});
