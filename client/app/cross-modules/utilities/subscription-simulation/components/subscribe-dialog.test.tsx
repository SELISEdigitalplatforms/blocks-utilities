import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import type {
  SimulatedSubscription,
  SubscriptionPurchasePreview,
} from "../models/subscription-simulation.model";

const toast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({ toast: (...args: unknown[]) => toast(...args) }));

const previewSubscription = vi.fn();
const subscribe = vi.fn();

vi.mock("../services/subscription-simulation.service", async () => {
  const actual = await vi.importActual<
    typeof import("../services/subscription-simulation.service")
  >("../services/subscription-simulation.service");

  return {
    ...actual,
    subscriptionSimulationService: {
      previewSubscription: (...args: unknown[]) => previewSubscription(...args),
      subscribe: (...args: unknown[]) => subscribe(...args),
    },
  };
});

import { SubscribeDialog } from "./subscribe-dialog";

const plan = {
  code: "professional",
  displayName: "Professional",
  quantityItems: [
    { itemKey: "seat", unitLabel: "seat", minQuantity: 1, maxQuantity: null, defaultQuantity: 1 },
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
  discountMinor: 0,
  builtInDiscountMinor: 0,
  promotionalDiscountMinor: 0,
  taxMinor: 0,
  totalDueNowMinor: 8_900,
  prorated: false,
  coveredDays: null,
  totalDays: null,
  periodStartUtc: "2026-08-16T00:00:00Z",
  periodEndUtc: "2026-09-16T00:00:00Z",
  nextRenewalAtUtc: "2026-09-16T00:00:00Z",
  nextRenewalAmountMinor: 8_900,
  trialEndsAtUtc: null,
  requiresCardSetup: false,
  pendingAnnualPeriod: null,
  blockers: [],
  quotedAtUtc: "2026-08-16T00:00:00Z",
  quoteValidUntilUtc: null,
};

const subscription: SimulatedSubscription = {
  subscriptionId: "sub-1",
  status: "Incomplete",
  planCode: "professional",
  planName: "Professional",
  currencyCode: "CHF",
  unitAmountMinor: 8_900,
  interval: "Month",
  intervalCount: 1,
  displayPriceNote: null,
  quantities: [{ itemKey: "seat", quantity: 1, unitLabel: "seat" }],
  currentPeriodStartUtc: "2026-08-16T00:00:00Z",
  currentPeriodEndUtc: "2026-09-16T00:00:00Z",
  nextPaymentAtUtc: "2026-09-16T00:00:00Z",
  trialEndsAtUtc: null,
  cancelAtPeriodEnd: false,
  canceledAtUtc: null,
  pendingQuantityChange: null,
  currentTier: null,
  recurringAmountMinor: 8_900,
  checkoutUrl: "https://checkout.example/session",
  version: 1,
};

const onSubscribed = vi.fn();

const renderDialog = () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={client}>
      <SubscribeDialog
        plan={plan}
        organizationId="org-1"
        open
        onOpenChange={() => {}}
        onSubscribed={onSubscribed}
      />
    </QueryClientProvider>,
  );
};

const click = (name: RegExp) => fireEvent.click(screen.getByRole("button", { name }));

beforeEach(() => {
  vi.clearAllMocks();
});

describe("SubscribeDialog", () => {
  it("cannot be confirmed before a preview is taken", () => {
    renderDialog();

    expect(screen.getByRole("button", { name: /^Subscribe$/ })).toBeDisabled();
  });

  it("previews the total due now before enabling confirm", async () => {
    previewSubscription.mockResolvedValue(quote);

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("subscribe-quote")).toBeInTheDocument();
    });

    expect(
      screen.getByTestId("subscribe-quote").textContent,
    ).toContain("89.00");
    expect(screen.getByRole("button", { name: /^Subscribe$/ })).toBeEnabled();
    // The preview writes nothing.
    expect(subscribe).not.toHaveBeenCalled();
  });

  it("discards the quote when the quantity is edited after previewing", async () => {
    previewSubscription.mockResolvedValue(quote);

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("subscribe-quote")).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText(/seat/), { target: { value: "2" } });

    expect(screen.queryByTestId("subscribe-quote")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Subscribe$/ })).toBeDisabled();
  });

  it("discards the quote when the discount code is edited after previewing", async () => {
    previewSubscription.mockResolvedValue(quote);

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("subscribe-quote")).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText(/Discount code/), { target: { value: "LAUNCH20" } });

    expect(screen.queryByTestId("subscribe-quote")).not.toBeInTheDocument();
  });

  it("sends exactly what was previewed when confirmed", async () => {
    previewSubscription.mockResolvedValue(quote);
    subscribe.mockResolvedValue(subscription);

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => screen.getByTestId("subscribe-quote"));

    click(/^Subscribe$/);

    await waitFor(() => {
      expect(subscribe).toHaveBeenCalledWith(
        expect.objectContaining({
          planCode: "professional",
          priceId: "price-1",
          quantities: [{ itemKey: "seat", quantity: 1 }],
          organizationId: "org-1",
        }),
      );
    });

    expect(onSubscribed).toHaveBeenCalledWith(subscription.checkoutUrl);
  });

  it("shows a blocker without disabling the preview, but keeps confirm disabled", async () => {
    previewSubscription.mockResolvedValue({
      ...quote,
      blockers: [
        {
          code: "subscription_billing_profile_incomplete",
          message: "This organization's billing profile is missing details an invoice must carry.",
          fields: { BillingProfile: ["LegalName"] },
        },
      ],
    });

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => {
      expect(
        screen.getByText(/billing profile is missing details/),
      ).toBeInTheDocument();
    });

    // The price was shown, but confirming it would be refused.
    expect(screen.getByRole("button", { name: /^Subscribe$/ })).toBeDisabled();
    expect(subscribe).not.toHaveBeenCalled();
  });

  it("reports a card requirement even when nothing is due now", async () => {
    previewSubscription.mockResolvedValue({
      ...quote,
      totalDueNowMinor: 0,
      trialEndsAtUtc: "2026-08-30T00:00:00Z",
      requiresCardSetup: true,
    });

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByText(/a card is required to start this subscription/)).toBeInTheDocument();
    });
  });
});
