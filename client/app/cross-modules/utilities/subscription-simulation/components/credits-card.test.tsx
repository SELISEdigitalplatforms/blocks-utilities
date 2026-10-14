import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type {
  CreditQuote,
  MeterTerms,
  SimulatedSubscription,
} from "../models/subscription-simulation.model";
import { subscriptionCreditsService } from "../services/subscription-credits.service";
import { CreditsCard } from "./credits-card";

/**
 * Buying meter credits from the simulation harness (spec 002, AC-31).
 *
 * Guards against a tester being shown a buy action for credits their subscription cannot buy, a
 * purchase going ahead without the quote — and its expiry date — being seen first, and a retried
 * purchase being sent under a fresh key, which would charge the card twice.
 */
vi.mock("../services/subscription-credits.service", () => ({
  subscriptionCreditsService: {
    listGrants: vi.fn(),
    previewPurchase: vi.fn(),
    purchase: vi.fn(),
    grant: vi.fn(),
    move: vi.fn(),
  },
}));

vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));

const service = vi.mocked(subscriptionCreditsService);

const meter = (overrides: Partial<MeterTerms> = {}): MeterTerms => ({
  meterKey: "ai-credit",
  displayName: "AI",
  unitLabel: "credit",
  includedQuantity: 100,
  resetPolicy: "Periodic",
  carryForwardCap: null,
  overageAllowed: false,
  overagePricing: null,
  creditPurchase: {
    currencyCode: "USD",
    unitAmountMinor: 10,
    unitAmount: "0.10",
    minQuantity: 10,
    maxQuantity: 1_000,
    expiryPolicy: "Periodic",
    taxRateBasisPoints: null,
    taxMode: null,
  },
  ...overrides,
});

const subscription = (meters: MeterTerms[]): SimulatedSubscription =>
  ({
    subscriptionId: "sub-1",
    status: "Active",
    planCode: "pro",
    planName: "Pro",
    currencyCode: "USD",
    meters,
    version: 1,
  }) as unknown as SimulatedSubscription;

const quote: CreditQuote = {
  meterKey: "ai-credit",
  seatNumber: null,
  quantity: 100,
  currencyCode: "USD",
  unitAmountMinor: 10,
  grossMinor: 1_000,
  discountMinor: 200,
  netMinor: 800,
  taxMinor: 0,
  totalMinor: 800,
  taxRateBasisPoints: null,
  taxMode: null,
  discountCode: "topup20",
  expiryPolicy: "Periodic",
  expiresAtUtc: "2026-11-01T00:00:00Z",
};

const renderCard = (meters: MeterTerms[]) =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <CreditsCard subscription={subscription(meters)} organizationId={undefined} />
    </QueryClientProvider>,
  );

describe("the credits card", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    service.listGrants.mockResolvedValue([]);
  });

  it("offers to buy only credits the subscription can buy in its own currency", () => {
    renderCard([meter(), meter({ meterKey: "storage", displayName: "Storage", creditPurchase: null })]);

    expect(screen.getAllByRole("button", { name: "Buy credits" })).toHaveLength(1);
    expect(screen.getByText("Credits for this meter are not for sale in USD.")).toBeInTheDocument();
  });

  it("shows the quote and its expiry before it will buy", async () => {
    const user = userEvent.setup();
    service.previewPurchase.mockResolvedValue(quote);
    renderCard([meter()]);

    await user.click(screen.getByRole("button", { name: "Buy credits" }));
    await user.type(screen.getByLabelText("Discount code"), "topup20");
    await user.click(screen.getByRole("button", { name: "Get quote" }));

    expect(await screen.findByLabelText("Quote")).toHaveTextContent("−$2.00");
    expect(screen.getByText(/These credits expire on/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Buy for/ })).toBeInTheDocument();
    expect(service.purchase).not.toHaveBeenCalled();
  });

  it("sends a retried purchase under the key it was quoted with", async () => {
    const user = userEvent.setup();
    service.previewPurchase.mockResolvedValue(quote);
    service.purchase.mockRejectedValueOnce(new Error("Timed out."));
    renderCard([meter()]);

    await user.click(screen.getByRole("button", { name: "Buy credits" }));
    await user.click(screen.getByRole("button", { name: "Get quote" }));
    await user.click(await screen.findByRole("button", { name: /Buy for/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Timed out.");

    service.purchase.mockResolvedValueOnce({
      purchaseId: "sub-1:key",
      status: "Finalized",
      quote,
      paymentId: "pay-1",
      grant: null,
      failureCode: null,
      failureReason: null,
      createdAtUtc: "2026-10-13T12:00:00Z",
      replayed: true,
    });
    await user.click(screen.getByRole("button", { name: /Buy for/ }));

    await waitFor(() => expect(service.purchase).toHaveBeenCalledTimes(2));
    const [first, second] = service.purchase.mock.calls.map(([, request]) => request.idempotencyKey);
    expect(first).toBeTruthy();
    expect(second).toBe(first);
  });

  it("asks for a new quote once the request is changed", async () => {
    const user = userEvent.setup();
    service.previewPurchase.mockResolvedValue(quote);
    renderCard([meter()]);

    await user.click(screen.getByRole("button", { name: "Buy credits" }));
    await user.click(screen.getByRole("button", { name: "Get quote" }));
    await screen.findByLabelText("Quote");
    await user.type(screen.getByLabelText("Credits"), "0");

    expect(screen.queryByLabelText("Quote")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Get quote" })).toBeInTheDocument();
  });

  it("will not grant credits without a reason", async () => {
    const user = userEvent.setup();
    renderCard([meter()]);

    await user.click(screen.getByRole("button", { name: "Grant" }));

    expect(screen.getByRole("button", { name: "Grant credits" })).toBeDisabled();
  });

  it("lists the subscription's grants with what is left of each", async () => {
    service.listGrants.mockResolvedValue([
      {
        grantId: "g1",
        meterKey: "ai-credit",
        seatNumber: 2,
        source: "Purchased",
        quantity: 100,
        remaining: 45,
        expiryPolicy: "Never",
        expiresAtUtc: null,
        status: "Active",
        grantedAtUtc: "2026-10-13T12:00:00Z",
        grantedByUserId: "user-1",
        reason: null,
        purchaseId: "p1",
        sourceGrantId: null,
      },
    ]);
    renderCard([meter()]);

    expect(await screen.findByText("45 / 100")).toBeInTheDocument();
    expect(screen.getByText("Seat 2")).toBeInTheDocument();
  });
});
