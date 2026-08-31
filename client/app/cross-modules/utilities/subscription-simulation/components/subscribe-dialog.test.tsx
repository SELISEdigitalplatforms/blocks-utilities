import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router";
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
  netSubtotalMinor: 8_900,
  tax: null,
  totalDueNowMinor: 8_900,
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

const subscription: SimulatedSubscription = {
  subscriptionId: "sub-1",
  status: "Incomplete",
  planCode: "professional",
  planName: "Professional",
  currencyCode: "CHF",
  unitAmountMinor: 8_900,
  interval: "Month",
  intervalCount: 1,
  usageInterval: "Month",
  usageIntervalCount: 1,
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
    <MemoryRouter initialEntries={["/app/project-1/subscription/plans?organizationId=org-1"]}>
      <Routes>
        <Route
          path="/app/:itemId/subscription/plans"
          element={
            <QueryClientProvider client={client}>
              <SubscribeDialog
                plan={plan}
                organizationId="org-1"
                open
                onOpenChange={() => {}}
                onSubscribed={onSubscribed}
              />
            </QueryClientProvider>
          }
        />
      </Routes>
    </MemoryRouter>,
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

  it("does not collect a second billing contact", () => {
    renderDialog();

    expect(screen.queryByLabelText(/Billing email/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Billing name/i)).not.toBeInTheDocument();
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
      expect(screen.getByTestId("billing-profile-incomplete")).toBeInTheDocument();
    });

    expect(screen.getByRole("link", { name: /Complete the billing profile/ })).toHaveAttribute(
      "href",
      "/app/project-1/subscription/billing-profile?organizationId=org-1",
    );

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

  it("explains a campaign discount and when standard pricing resumes", async () => {
    previewSubscription.mockResolvedValue({
      ...quote,
      totalDueNowMinor: 0,
      campaign: {
        kind: "FreeOpeningCalendarPeriod",
        description: "Your first calendar month is free. Standard pricing begins once this " +
          "opening period ends.",
        discountEndsAtUtc: "2026-09-01T00:00:00Z",
        temporaryEntitlementKey: "seats",
        temporaryEntitlementLimit: 1,
      },
    });

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("subscribe-quote-campaign")).toBeInTheDocument();
    });

    const panel = screen.getByTestId("subscribe-quote-campaign");
    expect(panel.textContent).toContain("Your first calendar month is free");
    expect(panel.textContent).toContain("seats limited to 1");
  });

  it("shows no campaign panel for an ordinary discount code", async () => {
    previewSubscription.mockResolvedValue(quote);

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("subscribe-quote")).toBeInTheDocument();
    });

    expect(screen.queryByTestId("subscribe-quote-campaign")).not.toBeInTheDocument();
  });

  it("renders an exclusive tax's percentage, mode and amount", async () => {
    previewSubscription.mockResolvedValue({
      ...quote,
      netSubtotalMinor: 8_900,
      tax: { rateBasisPoints: 810, mode: "Exclusive", amountMinor: 721 },
      totalDueNowMinor: 9_621,
    });

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("subscribe-quote")).toBeInTheDocument();
    });

    const panel = screen.getByTestId("subscribe-quote");
    expect(panel.textContent).toContain("VAT (8.1%, added)");
    expect(panel.textContent).toContain("7.21");
    expect(panel.textContent).toContain("96.21");
  });

  it("renders an inclusive tax's percentage, mode and amount", async () => {
    previewSubscription.mockResolvedValue({
      ...quote,
      netSubtotalMinor: 8_179,
      tax: { rateBasisPoints: 810, mode: "Inclusive", amountMinor: 721 },
    });

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("subscribe-quote")).toBeInTheDocument();
    });

    expect(screen.getByTestId("subscribe-quote").textContent).toContain("VAT (8.1%, included)");
  });

  it("renders configured zero tax during a card-free trial rather than hiding it", async () => {
    previewSubscription.mockResolvedValue({
      ...quote,
      totalDueNowMinor: 0,
      trialEndsAtUtc: "2026-08-30T00:00:00Z",
      tax: { rateBasisPoints: 810, mode: "Exclusive", amountMinor: 0 },
      nextRenewal: {
        ...quote.nextRenewal,
        tax: { rateBasisPoints: 810, mode: "Exclusive", amountMinor: 721 },
      },
    });

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("subscribe-quote")).toBeInTheDocument();
    });

    // The zero-amount due-now tax still renders -- an unconfigured price is the only case that
    // hides the row, not a zero amount on a configured one.
    const rows = screen.getAllByText("VAT (8.1%, added)");
    expect(rows.length).toBe(2); // once for due now, once for the first renewal
  });

  it("hides tax only when the price carries no tax configuration at all", async () => {
    previewSubscription.mockResolvedValue(quote); // quote.tax is null

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("subscribe-quote")).toBeInTheDocument();
    });

    expect(screen.queryByText(/VAT/)).not.toBeInTheDocument();
  });

  it("renders separate due-now and renewal breakdowns rather than a single renewal total", async () => {
    previewSubscription.mockResolvedValue({
      ...quote,
      subtotalMinor: 8_900,
      netSubtotalMinor: 8_900,
      totalDueNowMinor: 8_900,
      nextRenewalAmountMinor: 9_621,
      nextRenewal: {
        subtotalMinor: 8_900,
        builtInDiscountMinor: 0,
        promotionalDiscountMinor: 0,
        discountMinor: 0,
        netSubtotalMinor: 8_900,
        tax: { rateBasisPoints: 810, mode: "Exclusive", amountMinor: 721 },
        totalMinor: 9_621,
        renewalAtUtc: "2026-09-16T00:00:00Z",
      },
    });

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("subscribe-quote")).toBeInTheDocument();
    });

    const panel = screen.getByTestId("subscribe-quote");
    // Due now has no tax; the renewal breakdown does -- the two breakdowns must not collapse
    // into one, so exactly one VAT line renders, on the renewal side.
    expect(screen.getAllByText(/VAT/).length).toBe(1);
    expect(panel.textContent).toContain("Next renewal");
    expect(panel.textContent).toContain("96.21");
  });

  it("preserves the existing discount, proration, campaign and total displays", async () => {
    previewSubscription.mockResolvedValue({
      ...quote,
      subtotalMinor: 10_000,
      builtInDiscountMinor: 500,
      promotionalDiscountMinor: 300,
      discountMinor: 800,
      netSubtotalMinor: 9_200,
      totalDueNowMinor: 9_200,
      prorated: true,
      coveredDays: 7,
      totalDays: 31,
    });

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("subscribe-quote")).toBeInTheDocument();
    });

    const panel = screen.getByTestId("subscribe-quote");
    expect(panel.textContent).toContain("Built-in discount");
    expect(panel.textContent).toContain("Promotional discount");
    expect(panel.textContent).toContain("Net subtotal");
    expect(panel.textContent).toContain("7/31 days");
    expect(panel.textContent).toContain("92.00");
  });

  it("labels a prorated trial-conversion stub separately, without changing the recurring price shown", async () => {
    previewSubscription.mockResolvedValue({
      ...quote,
      trialEndsAtUtc: "2026-09-25T00:00:00Z",
      nextRenewalAtUtc: "2026-09-25T00:00:00Z",
      // The full, un-prorated recurring price -- unchanged from what a client already reading
      // only nextRenewal/nextRenewalAmountMinor must keep seeing.
      nextRenewalAmountMinor: 8_900,
      nextRenewal: {
        subtotalMinor: 8_900,
        builtInDiscountMinor: 0,
        promotionalDiscountMinor: 0,
        discountMinor: 0,
        netSubtotalMinor: 8_900,
        tax: null,
        totalMinor: 8_900,
        renewalAtUtc: "2026-09-25T00:00:00Z",
      },
      // The actual next charge: a 6/30-day stub, genuinely cheaper than the recurring price above.
      nextCharge: {
        chargeAtUtc: "2026-09-25T00:00:00Z",
        periodStartUtc: "2026-09-25T00:00:00Z",
        periodEndUtc: "2026-10-01T00:00:00Z",
        prorated: true,
        coveredDays: 6,
        totalDays: 30,
        subtotalMinor: 1_780,
        builtInDiscountMinor: 0,
        promotionalDiscountMinor: 0,
        discountMinor: 0,
        netSubtotalMinor: 1_780,
        tax: null,
        totalMinor: 1_780,
      },
    });

    renderDialog();
    click(/^Preview$/);

    await waitFor(() => {
      expect(screen.getByTestId("subscribe-quote")).toBeInTheDocument();
    });

    const panel = screen.getByTestId("subscribe-quote");
    expect(panel.textContent).toContain("First charge after trial");
    expect(panel.textContent).toContain("6/30 days");
    expect(panel.textContent).toContain("17.80"); // the prorated stub.
    // The recurring price is shown too, relabeled and unaffected by the stub above it.
    expect(panel.textContent).toContain("Recurring price");
    expect(panel.textContent).toContain("89.00");
  });
});
