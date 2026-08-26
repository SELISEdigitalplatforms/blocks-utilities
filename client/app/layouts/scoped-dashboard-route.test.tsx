import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { Menu } from "@/models/menu-models";

const received = vi.fn();

vi.mock("@seliseblocks/genesis-os/layouts", () => ({
  DashboardRoute: (props: { navigationMenus: Menu[] }) => {
    received(props.navigationMenus);
    return null;
  },
}));

import { ScopedDashboardRoute } from "./scoped-dashboard-route";

const menus: Menu[] = [
  {
    id: "subscription-invoices",
    type: "menu",
    name: "Invoices",
    path: "/app/subscription/invoices",
  },
];

describe("scoped dashboard route", () => {
  it("hands the shell subscription links that keep the organization in view", () => {
    render(
      <MemoryRouter
        initialEntries={["/app/project-1/subscription/plans?organizationId=org-1"]}
      >
        <ScopedDashboardRoute navigationMenus={menus} redirectPaths={{}} />
      </MemoryRouter>,
    );

    // The sidebar is rendered by the shell, above every page, so a page cannot reach the links that
    // navigate away from it. This is the one place the menu tree and the current URL are both in
    // hand.
    expect(received).toHaveBeenCalledWith([
      expect.objectContaining({ path: "/app/subscription/invoices?organizationId=org-1" }),
    ]);
  });

  it("changes nothing when no organization is being looked at", () => {
    render(
      <MemoryRouter initialEntries={["/app/project-1/dashboard"]}>
        <ScopedDashboardRoute navigationMenus={menus} redirectPaths={{}} />
      </MemoryRouter>,
    );

    expect(received).toHaveBeenLastCalledWith(menus);
  });
});
