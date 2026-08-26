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
