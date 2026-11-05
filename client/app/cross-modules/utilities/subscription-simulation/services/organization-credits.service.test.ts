import { HttpError } from "@seliseblocks/genesis-os/lib";
import { describe, expect, it, vi } from "vitest";

const http = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }));

vi.mock("@/lib/http-client", () => ({ serviceInstances: { utitlitiesService: http } }));

import { organizationCreditsService } from "./organization-credits.service";
import { SubscriptionOperationError } from "./subscription-simulation.service";

/**
 * Standalone credits (spec 005). Guards the routes having no subscription in them, the
 * organization scope reaching the server, and a refusal keeping the server's own code — the buy
 * dialog decides from it whether a retry may reuse its idempotency key.
 */
describe("organizationCreditsService", () => {
  it("addresses the organization routes with the scope named", async () => {
    http.get.mockResolvedValue({ success: true, data: [], error: null });
    http.post.mockResolvedValue({ success: true, data: { totalMinor: 1000 }, error: null });

    await organizationCreditsService.listGrants("org-1");
    await organizationCreditsService.listPurchases();
    await organizationCreditsService.listInvoices("org-1");
    await organizationCreditsService.previewPurchase({ meterKey: "ai", quantity: 100, currencyCode: "USD" });

    expect(http.get).toHaveBeenNthCalledWith(1, "/api/organization-credits?organizationId=org-1");
    expect(http.get).toHaveBeenNthCalledWith(2, "/api/organization-credits/purchases");
    expect(http.get).toHaveBeenNthCalledWith(3, "/api/organization-credits/invoices?organizationId=org-1&pageSize=25");
    expect(http.post).toHaveBeenCalledWith("/api/organization-credits/preview", expect.objectContaining({ currencyCode: "USD" }));
  });

  it("keeps the server's code on a refused purchase", async () => {
    http.post.mockRejectedValue(
      new HttpError(409, {
        errors: {
          success: false,
          data: null,
          error: { code: "subscription_credits_exhausted", message: "No credits left for this meter." },
        } as unknown as Record<string, string>,
      }),
    );

    const failure = await organizationCreditsService
      .purchase({ meterKey: "ai", quantity: 1, currencyCode: "USD", idempotencyKey: "k" })
      .catch((error: unknown) => error);

    expect(failure).toBeInstanceOf(SubscriptionOperationError);
    expect((failure as SubscriptionOperationError).code).toBe("subscription_credits_exhausted");
  });
});
