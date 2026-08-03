import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { Home } from "lucide-react";
import { DesktopMenuItem } from "./desktop-menu-item";
import type { Menu } from "@/models/menu-models";

const leaf: Extract<Menu, { type: "menu" }> = {
  type: "menu",
  id: "settings",
  name: "Settings",
  path: "/settings",
  icon: Home,
  badge: "new",
};

const parent: Extract<Menu, { type: "menu" }> = {
  type: "menu",
  id: "auth",
  name: "Authentication",
  path: "/auth",
  icon: Home,
  children: [
    { type: "menu", id: "sso", name: "SSO", path: "/auth/sso" },
    { type: "separator", id: "sep" },
    { type: "menu", id: "off", name: "Disabled", path: "/auth/off", disabled: true },
  ],
};

const renderAt = (menu: Extract<Menu, { type: "menu" }>, open: boolean, route = "/") =>
  render(
    <MemoryRouter initialEntries={[route]}>
      <DesktopMenuItem menu={menu} isSidebarOpen={open} />
    </MemoryRouter>,
  );

describe("DesktopMenuItem", () => {
  it("renders a leaf menu with its badge when the sidebar is open", () => {
    renderAt(leaf, true, "/settings");
    expect(screen.getByText("Settings")).toBeInTheDocument();
    expect(screen.getByText("new")).toBeInTheDocument();
    expect(screen.getByRole("link")).toHaveAttribute("href", "/settings");
  });

  it("renders a collapsed leaf with a hover tooltip label", () => {
    renderAt(leaf, false);
    expect(screen.getAllByText("Settings").length).toBeGreaterThanOrEqual(1);
  });

  it("renders a parent menu with only its enabled children", () => {
    renderAt(parent, true, "/auth/sso");
    expect(screen.getByText("SSO")).toBeInTheDocument();
    expect(screen.queryByText("Disabled")).not.toBeInTheDocument();
  });
});
