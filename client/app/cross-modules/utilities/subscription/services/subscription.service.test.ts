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

  describe("narrowing to one product family", () => {
    it("sends the family code when one is given", async () => {
      getMock.mockResolvedValue(ok([]));

      await subscriptionService.listPlans(undefined, undefined, "growth");

      expect(getMock).toHaveBeenCalledWith("/api/subscription-plans?familyCode=growth");
    });

    it("combines the family with the organization and the status", async () => {
      getMock.mockResolvedValue(ok([]));

      await subscriptionService.listPlans("org-1", "All", "growth");

      expect(getMock).toHaveBeenCalledWith(
        "/api/subscription-plans?organizationId=org-1&status=All&familyCode=growth",
      );
    });

    /**
     * A blank field is not a family. Sending it would be harmless — the server reads blank as
     * every family — but the request a screen makes should say what it means.
     */
    it.each(["", "   "])("omits a blank family code (%s)", async (blank) => {
      getMock.mockResolvedValue(ok([]));

      await subscriptionService.listPlans(undefined, undefined, blank);

      expect(getMock).toHaveBeenCalledWith("/api/subscription-plans");
    });

    /**
     * Byte-identical to the request made before this parameter existed, which is what keeps every
     * screen that does not care about families untouched.
     */
    it("sends nothing extra when no family is named", async () => {
      getMock.mockResolvedValue(ok([]));

      await subscriptionService.listPlans();

      expect(getMock).toHaveBeenCalledWith("/api/subscription-plans");
    });

    /** Sent as authored, since the server matches it exactly. */
    it("does not fold the case of a family code", async () => {
      getMock.mockResolvedValue(ok([]));

      await subscriptionService.listPlans(undefined, undefined, "Growth");

      expect(getMock).toHaveBeenCalledWith("/api/subscription-plans?familyCode=Growth");
    });

    it("encodes a family code that needs it", async () => {
      getMock.mockResolvedValue(ok([]));

      await subscriptionService.listPlans(undefined, undefined, "a/b c");

      expect(getMock).toHaveBeenCalledWith("/api/subscription-plans?familyCode=a%2Fb+c");
    });
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
