import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { Home } from "lucide-react";
import { MobileMenuItem } from "./mobile-menu-item";
import type { Menu } from "@/models/menu-models";

const leaf: Extract<Menu, { type: "menu" }> = {
  type: "menu",
  id: "settings",
  name: "Settings",
  path: "/settings",
  icon: Home,
  badge: "beta",
};

const parent: Extract<Menu, { type: "menu" }> = {
  type: "menu",
  id: "auth",
  name: "Authentication",
  path: "/auth",
  children: [{ type: "menu", id: "sso", name: "SSO", path: "/auth/sso" }],
};

describe("MobileMenuItem", () => {
  it("renders a leaf link and fires the click handler", async () => {
    const onClick = vi.fn();
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <MobileMenuItem menu={leaf} onClick={onClick} />
      </MemoryRouter>,
    );
    expect(screen.getByText("beta")).toBeInTheDocument();
    await user.click(screen.getByRole("link"));
    expect(onClick).toHaveBeenCalled();
  });

  it("opens a sheet with the child items for a parent menu", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <MobileMenuItem menu={parent} />
      </MemoryRouter>,
    );
    await user.click(screen.getByText("Authentication"));
    expect(await screen.findByText("SSO")).toBeInTheDocument();
  });
});
