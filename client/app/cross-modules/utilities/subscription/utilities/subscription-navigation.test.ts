import { describe, expect, it } from "vitest";
import type { Menu } from "@/models/menu-models";
import { withSubscriptionOrganizationScope } from "./subscription-navigation";

const menus = (): Menu[] => [
  {
    id: "payment",
    type: "menu",
    name: "Payment",
    path: "/app/payment/list",
  },
  {
    id: "subscription",
    type: "menu",
    name: "Subscriptions",
    path: "/app/subscription/plans",
    children: [
      { id: "plans", type: "menu", name: "Plans", path: "/app/subscription/plans" },
      { id: "discounts", type: "menu", name: "Discounts", path: "/app/subscription/discounts" },
      { id: "invoices", type: "menu", name: "Invoices", path: "/app/subscription/invoices" },
      {
        id: "billing-profile",
        type: "menu",
        name: "Billing profile",
        path: "/app/subscription/billing-profile",
      },
      { id: "sep", type: "separator" },
    ],
  },
];

const child = (result: Menu[], id: string): Extract<Menu, { type: "menu" }> => {
  const parent = result[1];
  if (parent.type !== "menu") throw new Error("the subscription group is a menu");

  const found = parent.children?.find((entry) => entry.id === id);
  if (found?.type !== "menu") throw new Error(`no menu child ${id}`);

  return found;
};

describe("subscription menu scope", () => {
  it("carries the organization onto subscription links", () => {
    const result = withSubscriptionOrganizationScope(
      menus(),
      "org-1",
      "/app/project-1/subscription/plans",
    );

    expect(child(result, "invoices").path).toBe(
      "/app/subscription/invoices?organizationId=org-1",
    );
    expect(child(result, "billing-profile").path).toBe(
      "/app/subscription/billing-profile?organizationId=org-1",
    );
  });

  it("leaves other modules alone", () => {
    const result = withSubscriptionOrganizationScope(
      menus(),
      "org-1",
      "/app/project-1/subscription/plans",
    );

    // The scope is this module's idea. Putting it on Payment's link would advertise a filter
    // Payment does not apply.
    const payment = result[0];
    expect(payment.type === "menu" && payment.path).toBe("/app/payment/list");
  });

  it("leaves the entry the page is already inside clean", () => {
    const result = withSubscriptionOrganizationScope(
      menus(),
      "org-1",
      "/app/project-1/subscription/plans/create",
    );

    // The sidebar marks an entry active by testing pathname.startsWith(menu.path); a query string
    // appended there matches nothing and Plans would stop being highlighted while the reader is
    // plainly inside it. Nothing is lost — that link navigates nowhere.
    expect(child(result, "plans").path).toBe("/app/subscription/plans");
    expect(child(result, "invoices").path).toBe(
      "/app/subscription/invoices?organizationId=org-1",
    );
  });

  it("returns the menus untouched when nothing is scoped", () => {
    const original = menus();

    expect(withSubscriptionOrganizationScope(original, undefined, "/app/project-1/dashboard")).toBe(
      original,
    );
  });

  it("escapes an organization id so it cannot forge another parameter", () => {
    const result = withSubscriptionOrganizationScope(
      menus(),
      "org 1&admin=true",
      "/app/project-1/dashboard",
    );

    expect(child(result, "invoices").path).toBe(
      "/app/subscription/invoices?organizationId=org%201%26admin%3Dtrue",
    );
  });

  /**
   * Discounts is scoped by the same rule as every other subscription entry, without being named in
   * it. Asserted anyway: the rule is a prefix test on the authored path, and an entry added under a
   * path that does not start with `/app/subscription` would silently lose the scope.
   */
  it("carries the organization onto the Discounts link", () => {
    const result = withSubscriptionOrganizationScope(
      menus(),
      "org-1",
      "/app/project-1/subscription/plans",
    );

    expect(child(result, "discounts").path).toBe(
      "/app/subscription/discounts?organizationId=org-1",
    );
  });

  /**
   * The highlighting case. The sidebar marks an entry active with
   * `pathname.startsWith(menu.path)`, so the moment a query string is appended to the entry the
   * reader is inside, the prefix stops matching and Discounts goes dark while the Discounts page is
   * on screen. The guarantee this module provides is the clean path; the two assertions below are
   * that guarantee and the prefix property that depends on it.
   */
  it("leaves the Discounts link clean while the Discounts page is open, so it stays highlighted", () => {
    const result = withSubscriptionOrganizationScope(
      menus(),
      "org-1",
      "/app/project-1/subscription/discounts",
    );
    const discounts = child(result, "discounts");

    expect(discounts.path).toBe("/app/subscription/discounts");
    expect(discounts.path).not.toContain("?");

    // Why the clean path matters, as a property of the prefix test rather than of this module: the
    // shell inserts the project segment into both sides before comparing, so model that here.
    const rendered = discounts.path.replace("/app/", "/app/project-1/");
    expect("/app/project-1/subscription/discounts".startsWith(rendered)).toBe(true);
    expect(
      "/app/project-1/subscription/discounts".startsWith(`${rendered}?organizationId=org-1`),
    ).toBe(false);
  });

  /** The converse: being on Discounts must not strip the scope from the links leaving it. */
  it("still scopes Plans and Invoices while the Discounts page is open", () => {
    const result = withSubscriptionOrganizationScope(
      menus(),
      "org-1",
      "/app/project-1/subscription/discounts",
    );

    expect(child(result, "plans").path).toBe("/app/subscription/plans?organizationId=org-1");
    expect(child(result, "invoices").path).toBe("/app/subscription/invoices?organizationId=org-1");
  });

  it("keeps separators as they are", () => {
    const result = withSubscriptionOrganizationScope(
      menus(),
      "org-1",
      "/app/project-1/dashboard",
    );
    const parent = result[1];

    expect(parent.type === "menu" && parent.children?.at(-1)?.type).toBe("separator");
  });
});
