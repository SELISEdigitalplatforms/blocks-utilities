import { describe, expect, it, vi } from "vitest";

const http = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }));

vi.mock("@/lib/http-client", () => ({ serviceInstances: { utitlitiesService: http } }));

import { meterCatalogueService } from "./meter-catalogue.service";

/**
 * The meter catalogue and credit offers (spec 005). Guards the one thing a wrong URL would
 * silently do: write an organization override to the tenant-wide scope, or the reverse.
 */
describe("meterCatalogueService", () => {
  it("scopes catalogue reads and writes to the organization named", async () => {
    http.get.mockResolvedValue({ success: true, data: [], error: null });
    http.put.mockResolvedValue({ success: true, data: { meterKey: "ai" }, error: null });

    await meterCatalogueService.listEntries("org-1");
    await meterCatalogueService.updateEntry(
      "ai",
      { displayName: "AI", unitLabel: "credit", aggregation: 0, quantityScale: 0, status: "Archived" },
      "org-1",
    );

    expect(http.get).toHaveBeenCalledWith("/api/meter-catalogue?organizationId=org-1");
    expect(http.put).toHaveBeenCalledWith(
      "/api/meter-catalogue/ai?organizationId=org-1",
      expect.objectContaining({ status: "Archived" }),
    );
  });

  it("reads the tenant-wide offers when no organization is named, and surfaces the server's message", async () => {
    http.get.mockResolvedValue({ success: true, data: [], error: null });
    await meterCatalogueService.listOffers();
    expect(http.get).toHaveBeenCalledWith("/api/credit-offers");

    http.post.mockResolvedValue({
      success: false,
      data: null,
      error: { code: "subscription_credit_offer_exists", message: "An offer for this meter already exists." },
    });

    await expect(
      meterCatalogueService.createOffer({
        meterKey: "ai",
        enabled: true,
        unitPrices: [{ currencyCode: "USD", unitAmountMinor: 10 }],
        minQuantity: 1,
        maxQuantity: 100,
        expiresAfter: null,
        spendOrder: "SubscriptionFirst",
      }),
    ).rejects.toThrow("An offer for this meter already exists.");
  });
});
