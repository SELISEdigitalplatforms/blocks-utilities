import { describe, expect, it } from "vitest";
import { withOrganizationScope } from "./use-organization-scope";

describe("withOrganizationScope", () => {
  it("leaves a tenant-wide link alone", () => {
    expect(withOrganizationScope("/app/t/subscription/plans", null)).toBe(
      "/app/t/subscription/plans",
    );
    expect(withOrganizationScope("/app/t/subscription/plans", undefined)).toBe(
      "/app/t/subscription/plans",
    );
  });

  /**
   * The whole point: a plan scoped to an organization is invisible to the console without this,
   * because the portal's own context resolves to the console organization instead.
   */
  it("names the organization so a scoped plan stays reachable", () => {
    expect(withOrganizationScope("/app/t/subscription/plans/plan-1", "org-1")).toBe(
      "/app/t/subscription/plans/plan-1?organizationId=org-1",
    );
  });

  it("escapes an organization id that would otherwise break the query string", () => {
    expect(withOrganizationScope("/plans", "org 1&x=2")).toBe(
      "/plans?organizationId=org%201%26x%3D2",
    );
  });
});
