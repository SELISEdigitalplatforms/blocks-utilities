import { beforeEach, describe, expect, it, vi } from "vitest";

const getMock = vi.fn();
const postMock = vi.fn();

vi.mock("@/lib/http-client", () => ({
  serviceInstances: {
    utitlitiesService: {
      get: (...args: unknown[]) => getMock(...args),
      post: (...args: unknown[]) => postMock(...args),
    },
  },
}));

import { subscriptionService } from "./subscription.service";

const ok = (data: unknown) => ({ success: true, data, error: null });

describe("subscriptionService", () => {
  beforeEach(() => {
    getMock.mockReset();
    postMock.mockReset();
  });

  it("asks for tenant-wide plans when no organization is named", async () => {
    getMock.mockResolvedValue(ok([]));

    await subscriptionService.listPlans();

    expect(getMock).toHaveBeenCalledWith("/api/subscription-plans");
  });

  /**
   * The server resolves the portal's own context to the console organization, so a plan scoped
   * elsewhere is only visible when the request names that organization explicitly.
   */
  it("names the organization when listing a scoped catalogue", async () => {
    getMock.mockResolvedValue(ok([]));

    await subscriptionService.listPlans("org-1");

    expect(getMock).toHaveBeenCalledWith("/api/subscription-plans?organizationId=org-1");
  });

  it("names the organization when reading one scoped plan", async () => {
    getMock.mockResolvedValue(ok({ planId: "plan-1" }));

    await subscriptionService.getPlan("plan-1", "org-1");

    expect(getMock).toHaveBeenCalledWith(
      "/api/subscription-plans/plan-1?organizationId=org-1",
    );
  });

  it("escapes ids rather than pasting them into the path", async () => {
    getMock.mockResolvedValue(ok({ planId: "a/b" }));

    await subscriptionService.getPlan("a/b", "org 1");

    expect(getMock).toHaveBeenCalledWith(
      "/api/subscription-plans/a%2Fb?organizationId=org%201",
    );
  });

  it("surfaces the server's own message when a plan cannot be read", async () => {
    getMock.mockResolvedValue({
      success: false,
      data: null,
      error: { code: "subscription_plan_not_found", message: "The plan does not exist." },
    });

    await expect(subscriptionService.getPlan("plan-1")).rejects.toThrow(
      "The plan does not exist.",
    );
  });
});
