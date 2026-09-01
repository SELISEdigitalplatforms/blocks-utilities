import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { SimulatedSubscription } from "../models/subscription-simulation.model";
import { CurrentSubscriptionCard } from "./current-subscription-card";

/**
 * The three moments the deferred-trial spec asks for, and the one rule that governs all of
 * them: a card session is never mistaken for a payment, and never offered twice at once.
 */
const baseSubscription: SimulatedSubscription = {
  subscriptionId: "sub-1",
  status: "Trialing",
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
  nextPaymentAtUtc: null,
  trialEndsAtUtc: "2026-09-08T00:00:00Z",
  cancelAtPeriodEnd: false,
  canceledAtUtc: null,
  pendingQuantityChange: null,
  currentTier: null,
  recurringAmountMinor: 8_900,
  checkoutUrl: null,
  pendingPlanChange: null,
  pendingCheckout: null,
  hasPaymentMethod: null,
  meters: [],
  version: 1,
};

const noop = () => undefined;

const renderCard = (subscription: SimulatedSubscription) =>
  render(
    <CurrentSubscriptionCard
      subscription={subscription}
      isLoading={false}
      isError={false}
      error={null}
      scopeLabel="Acme"
      onRetry={noop}
      onCancel={noop}
      onChangePlan={noop}
      onChangeQuantity={noop}
      onCancelPendingQuantityChange={noop}
      onCancelPendingPlanChange={noop}
      isCancelingPendingPlanChange={false}
      isCancelingPendingQuantityChange={false}
      onViewAuditTrail={noop}
      onAddPaymentMethod={noop}
      isStartingPaymentMethodSetup={false}
    />,
  );

describe("CurrentSubscriptionCard payment-method actions", () => {
  it("offers to add a payment method during a card-free trial that has none", () => {
    renderCard({ ...baseSubscription, status: "Trialing", hasPaymentMethod: false });

    expect(screen.getByRole("button", { name: /add payment method/i })).toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: /complete card setup/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /continue subscription/i }),
    ).not.toBeInTheDocument();
  });

  it("offers nothing once a card is already on file", () => {
    // The case status alone cannot tell apart from the one above: a card-required trial that
    // already collected one, or a card-free trial where the subscriber added one voluntarily.
    renderCard({ ...baseSubscription, status: "Trialing", hasPaymentMethod: true });

    expect(
      screen.queryByRole("button", { name: /add payment method/i }),
    ).not.toBeInTheDocument();
  });

  it("still offers to cancel an unpaid subscription, not only to recover it", () => {
    // The server has always allowed cancelling anything short of Canceled/IncompleteExpired.
    // Unpaid was simply never returned by GetCurrentAsync before #360, so this had no chance to
    // matter until the card started showing Unpaid subscriptions at all.
    renderCard({ ...baseSubscription, status: "Unpaid", hasPaymentMethod: false });

    expect(screen.getByRole("button", { name: /^cancel$/i })).toBeInTheDocument();
  });

  it("offers to recover an unpaid subscription by adding a card", () => {
    renderCard({ ...baseSubscription, status: "Unpaid", hasPaymentMethod: false });

    expect(
      screen.getByRole("button", { name: /add card and continue subscription/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /^add payment method$/i }),
    ).not.toBeInTheDocument();
  });

  it("surfaces an open card session instead of offering to start a second one", () => {
    // Whichever of the three cases opened it -- the session already open always wins, so a
    // subscriber part-way through Stripe sees where to finish rather than a button that would
    // open a competing one.
    renderCard({
      ...baseSubscription,
      status: "Unpaid",
      hasPaymentMethod: false,
      pendingCheckout: {
        purpose: "PaymentMethodSetup",
        state: "Pending",
        errorCode: null,
        checkoutUrl: "https://checkout.stripe.com/setup-1",
      },
    });

    const link = screen.getByRole("link", { name: /complete card setup/i });
    expect(link).toHaveAttribute("href", "https://checkout.stripe.com/setup-1");
    expect(
      screen.queryByRole("button", { name: /add card and continue subscription/i }),
    ).not.toBeInTheDocument();
  });

  it("shows the incomplete card-required trial's own setup session the same way", () => {
    renderCard({
      ...baseSubscription,
      status: "Incomplete",
      hasPaymentMethod: null,
      pendingCheckout: {
        purpose: "PaymentMethodSetup",
        state: "Pending",
        errorCode: null,
        checkoutUrl: "https://checkout.stripe.com/setup-2",
      },
    });

    expect(screen.getByRole("link", { name: /complete card setup/i })).toHaveAttribute(
      "href",
      "https://checkout.stripe.com/setup-2",
    );
  });

  it("calls the handler rather than navigating directly, so the caller controls the request", () => {
    const onAddPaymentMethod = vi.fn();

    render(
      <CurrentSubscriptionCard
        subscription={{ ...baseSubscription, status: "Trialing", hasPaymentMethod: false }}
        isLoading={false}
        isError={false}
        error={null}
        scopeLabel="Acme"
        onRetry={noop}
        onCancel={noop}
        onChangePlan={noop}
        onChangeQuantity={noop}
        onCancelPendingQuantityChange={noop}
        onCancelPendingPlanChange={noop}
        isCancelingPendingPlanChange={false}
        isCancelingPendingQuantityChange={false}
        onViewAuditTrail={noop}
        onAddPaymentMethod={onAddPaymentMethod}
        isStartingPaymentMethodSetup={false}
      />,
    );

    screen.getByRole("button", { name: /add payment method/i }).click();

    expect(onAddPaymentMethod).toHaveBeenCalledTimes(1);
  });

  it("disables the action while a setup session is being opened", () => {
    render(
      <CurrentSubscriptionCard
        subscription={{ ...baseSubscription, status: "Unpaid", hasPaymentMethod: false }}
        isLoading={false}
        isError={false}
        error={null}
        scopeLabel="Acme"
        onRetry={noop}
        onCancel={noop}
        onChangePlan={noop}
        onChangeQuantity={noop}
        onCancelPendingQuantityChange={noop}
        onCancelPendingPlanChange={noop}
        isCancelingPendingPlanChange={false}
        isCancelingPendingQuantityChange={false}
        onViewAuditTrail={noop}
        onAddPaymentMethod={noop}
        isStartingPaymentMethodSetup
      />,
    );

    expect(
      screen.getByRole("button", { name: /add card and continue subscription/i }),
    ).toBeDisabled();
  });
});

describe("CurrentSubscriptionCard scheduled plan change", () => {
  const scheduled: SimulatedSubscription = {
    ...baseSubscription,
    pendingPlanChange: {
      targetPlanCode: "premium",
      targetPlanName: "Premium",
      targetPriceId: "price-2",
      interval: "Month",
      intervalCount: 1,
      quantities: [],
      requestedAtUtc: "2026-09-15T00:00:00Z",
      effectiveAtUtc: "2026-10-01T00:00:00Z",
    },
  };

  /**
   * Without this the subscriber reloads, sees the plan they are on, and has no way to know a
   * different one is already booked — let alone to call it off.
   */
  it("names the plan being moved to and when, and says the current one still stands", () => {
    renderCard(scheduled);

    const banner = screen.getByTestId("pending-plan-change");
    expect(banner).toHaveTextContent(/Premium/);
    expect(banner).toHaveTextContent(/nothing is charged until then/i);
  });

  it("offers to keep the current plan", () => {
    const onCancelPendingPlanChange = vi.fn();

    render(
      <CurrentSubscriptionCard
        subscription={scheduled}
        isLoading={false}
        isError={false}
        error={null}
        scopeLabel="Acme"
        onRetry={noop}
        onCancel={noop}
        onChangePlan={noop}
        onChangeQuantity={noop}
        onCancelPendingQuantityChange={noop}
        isCancelingPendingQuantityChange={false}
        onCancelPendingPlanChange={onCancelPendingPlanChange}
        isCancelingPendingPlanChange={false}
        onViewAuditTrail={noop}
        onAddPaymentMethod={noop}
        isStartingPaymentMethodSetup={false}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: /keep current plan/i }));

    expect(onCancelPendingPlanChange).toHaveBeenCalledOnce();
  });

  it("shows nothing when no plan change is scheduled", () => {
    renderCard(baseSubscription);

    expect(screen.queryByTestId("pending-plan-change")).not.toBeInTheDocument();
  });
});
