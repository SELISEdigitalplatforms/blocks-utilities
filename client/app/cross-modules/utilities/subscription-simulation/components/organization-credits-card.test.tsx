import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { CreditOffer } from "../../subscription/models/meter-catalogue.model";
import { meterCatalogueService } from "../../subscription/services/meter-catalogue.service";
import type { CreditQuote } from "../models/subscription-simulation.model";
import { organizationCreditsService } from "../services/organization-credits.service";
import { OrganizationCreditsCard } from "./organization-credits-card";

/**
 * Standalone credits (spec 005). Guards against a purchase going ahead without the quote being
 * seen, and against the currency the buyer picked not being the one sent — an organization with
 * no subscription has no currency of its own, so the dialog's choice is the only one there is.
 */
vi.mock("../../subscription/services/meter-catalogue.service", () => ({
  meterCatalogueService: { listOffers: vi.fn() },
}));

vi.mock("../services/organization-credits.service", () => ({
  organizationCreditsService: {
    listGrants: vi.fn(),
    listPurchases: vi.fn(),
    previewPurchase: vi.fn(),
    purchase: vi.fn(),
    grant: vi.fn(),
  },
}));

vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));

const offers = vi.mocked(meterCatalogueService);
const credits = vi.mocked(organizationCreditsService);

const offer: CreditOffer = {
  offerId: "offer-1",
  organizationId: null,
  meterKey: "ai",
  displayName: "AI",
  unitLabel: "credit",
  enabled: true,
  unitPrices: [
    { currencyCode: "USD", unitAmountMinor: 10 },
    { currencyCode: "CHF", unitAmountMinor: 8 },
  ],
  minQuantity: 10,
  maxQuantity: 1000,
  expiresAfter: { count: 30, unit: "Day" },
  taxRateBasisPoints: null,
  taxMode: null,
  spendOrder: "SubscriptionFirst",
  packs: [],
  createdAtUtc: "2026-11-01T00:00:00Z",
  lastUpdatedAtUtc: "2026-11-01T00:00:00Z",
  version: 1,
};

const quote: CreditQuote = {
  meterKey: "ai",
  seatNumber: null,
  quantity: 100,
  currencyCode: "CHF",
  unitAmountMinor: 8,
  grossMinor: 800,
  discountMinor: 0,
  netMinor: 800,
  taxMinor: 0,
  totalMinor: 800,
  taxRateBasisPoints: null,
  taxMode: null,
  discountCode: null,
  expiryPolicy: "Never",
  expiresAtUtc: "2026-12-01T00:00:00Z",
  paymentPath: "StoredCard",
};

const renderCard = () =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <OrganizationCreditsCard organizationId="org-1" />
    </QueryClientProvider>,
  );

describe("OrganizationCreditsCard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    offers.listOffers.mockResolvedValue([offer]);
    credits.listGrants.mockResolvedValue([]);
    credits.listPurchases.mockResolvedValue([]);
    credits.previewPurchase.mockResolvedValue(quote);
  });

  it("quotes in the currency picked before it buys, and reuses the quote's key on the purchase", async () => {
    const user = userEvent.setup();
    credits.purchase.mockResolvedValue({
      purchaseId: "p-1",
      status: "Finalized",
      quote,
      paymentId: "pay-1",
      grant: null,
      failureCode: null,
      failureReason: null,
      createdAtUtc: "2026-11-05T00:00:00Z",
      replayed: false,
    });

    renderCard();
    await user.click(await screen.findByRole("button", { name: "Buy" }));

    // No purchase without a quote: the only primary action offered is the quote.
    expect(screen.queryByRole("button", { name: /Buy for/ })).toBeNull();

    await user.click(screen.getByRole("combobox", { name: "Currency" }));
    await user.click(await screen.findByRole("option", { name: /CHF/ }));
    await user.click(screen.getByRole("button", { name: "Get quote" }));

    await screen.findByRole("button", { name: /Buy for/ });
    expect(credits.previewPurchase).toHaveBeenCalledWith(
      expect.objectContaining({ organizationId: "org-1", meterKey: "ai", quantity: 100, currencyCode: "CHF" }),
    );

    await user.click(screen.getByRole("button", { name: /Buy for/ }));

    await waitFor(() => expect(credits.purchase).toHaveBeenCalledTimes(1));
    const sent = credits.purchase.mock.calls[0][0];
    expect(sent.currencyCode).toBe("CHF");
    expect(sent.idempotencyKey).toMatch(/[0-9a-f-]{36}/);
  });

  it("buys a pack by its code, at the pack's size, in a currency the pack is priced in", async () => {
    const user = userEvent.setup();
    offers.listOffers.mockResolvedValue([
      {
        ...offer,
        packs: [{ code: "bulk", displayName: "Bulk", quantity: 5000, unitPrices: [{ currencyCode: "CHF", unitAmountMinor: 6 }] }],
      },
    ]);

    renderCard();
    await user.click(await screen.findByRole("button", { name: "Buy" }));
    await user.click(screen.getByRole("combobox", { name: "Pack" }));
    await user.click(await screen.findByRole("option", { name: /Bulk/ }));
    await user.click(screen.getByRole("button", { name: "Get quote" }));

    await waitFor(() => expect(credits.previewPurchase).toHaveBeenCalledTimes(1));
    expect(credits.previewPurchase).toHaveBeenCalledWith(
      expect.objectContaining({ packCode: "bulk", quantity: 5000, currencyCode: "CHF" }),
    );
    expect(screen.getByLabelText("Credits")).toBeDisabled();
  });

  it("does not offer to buy from a paused offer", async () => {
    offers.listOffers.mockResolvedValue([{ ...offer, enabled: false }]);

    renderCard();

    expect(await screen.findByRole("button", { name: "Buy" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Grant" })).toBeEnabled();
  });
});
