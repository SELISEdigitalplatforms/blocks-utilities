import { beforeEach, describe, expect, it, vi } from "vitest";

const { get, put } = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn() }));

vi.mock("@/lib/http-client", () => ({
  serviceInstances: { utitlitiesService: { get, put } },
}));

import { subscriptionBillingService } from "./subscription-billing.service";

const ok = <T,>(data: T) => ({ success: true, data, error: null });

describe("billing profile requests", () => {
  beforeEach(() => {
    get.mockReset();
    put.mockReset();
  });

  it("forwards a console organization on the query string", async () => {
    get.mockResolvedValue(ok({ organizationId: "org-9" }));

    await subscriptionBillingService.getBillingProfile("org-9");

    expect(get).toHaveBeenCalledWith("/api/subscription-billing-profile?organizationId=org-9");
  });

  it("sends no organization when there is none", async () => {
    get.mockResolvedValue(ok({ organizationId: "org-1" }));

    await subscriptionBillingService.getBillingProfile();

    expect(get).toHaveBeenCalledWith("/api/subscription-billing-profile");
  });

  it("raises the server's message rather than a generic one", async () => {
    put.mockResolvedValue({
      success: false,
      data: null,
      error: { code: "subscription_billing_profile_invalid", message: "Legal name is required." },
    });

    await expect(
      subscriptionBillingService.updateBillingProfile({
        legalName: "",
        billingContactName: "Ada",
        billingContactEmail: "ada@x.test",
      }),
    ).rejects.toThrow("Legal name is required.");
  });
});

describe("document queries", () => {
  beforeEach(() => {
    get.mockReset();
  });

  it("omits every filter that is unset", async () => {
    // Omitted rather than sent empty: the server reads an absent filter as "all of them" and a blank
    // documentType as a value it has to refuse.
    get.mockResolvedValue(ok({ items: [], pageInfo: { pageSize: 25, hasNextPage: false } }));

    await subscriptionBillingService.listDocuments({ pageSize: 25 });

    expect(get).toHaveBeenCalledWith("/api/subscriptions/invoices?pageSize=25");
  });

  it("serialises every filter that is set", async () => {
    get.mockResolvedValue(ok({ items: [], pageInfo: { pageSize: 10, hasNextPage: false } }));

    await subscriptionBillingService.listDocuments({
      pageSize: 10,
      after: "cursor-2",
      subscriptionId: "sub-1",
      documentType: "CreditNote",
      status: "Refunded",
      issuedFromUtc: "2026-01-01T00:00:00Z",
      issuedToUtc: "2026-12-31T23:59:59Z",
      organizationId: "org-9",
    });

    const [url] = get.mock.calls[0];
    expect(url).toContain("documentType=CreditNote");
    expect(url).toContain("status=Refunded");
    expect(url).toContain("subscriptionId=sub-1");
    expect(url).toContain("after=cursor-2");
    expect(url).toContain("organizationId=org-9");
  });

  it("drops a null cursor rather than sending the string null", async () => {
    get.mockResolvedValue(ok({ items: [], pageInfo: { pageSize: 25, hasNextPage: false } }));

    await subscriptionBillingService.listDocuments({ pageSize: 25, after: null });

    expect(get.mock.calls[0][0]).not.toContain("after");
  });

  it("fetches a pdf through the authenticated client", async () => {
    const blob = new Blob(["%PDF"], { type: "application/pdf" });
    get.mockResolvedValue(blob);

    await expect(
      subscriptionBillingService.downloadDocumentPdf("doc 1", "org-9"),
    ).resolves.toBe(blob);

    // The id is escaped, because a document id reaches this from a URL and a path segment is not a
    // place to trust one.
    expect(get).toHaveBeenCalledWith(
      "/api/subscriptions/invoices/doc%201/pdf?organizationId=org-9",
    );
  });
});
