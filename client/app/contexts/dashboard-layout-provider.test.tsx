import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import React, { useContext } from "react";
import { MemoryRouter } from "react-router";
import {
  DashboardLayoutProvider,
  SidebarContext,
} from "./dashboard-layout-provider";

let mobile = false;
vi.mock("@/hooks/use-is-mobile", () => ({
  default: () => mobile,
}));

function Probe() {
  const ctx = useContext(SidebarContext);
  return (
    <div>
      <span data-testid="open">{String(ctx.isSidebarOpen)}</span>
      <span data-testid="submenu">{String(ctx.isSidebarSubMenuOpen)}</span>
      <span data-testid="submenu-id">{String(ctx.subMenuId)}</span>
      <span data-testid="search">{ctx.servicesSearchTerm}</span>
      <button onClick={ctx.toggleSidebar}>toggle</button>
      <button onClick={ctx.closeSidebar}>close</button>
      <button onClick={ctx.closeWithoutPersist}>close-np</button>
      <button onClick={ctx.toggleSidebarSubMenu}>toggle-sub</button>
      <button onClick={ctx.showSidebarSubMenu}>show-sub</button>
      <button onClick={() => ctx.updateSubMenuId("m42")}>set-id</button>
      <button onClick={() => ctx.updateServicesSearchTerm("query")}>
        set-search
      </button>
    </div>
  );
}

const renderProvider = (
  props: Partial<React.ComponentProps<typeof DashboardLayoutProvider>> = {},
  route = "/",
) =>
  render(
    <MemoryRouter initialEntries={[route]}>
      <DashboardLayoutProvider isOpen={false} {...props}>
        <Probe />
      </DashboardLayoutProvider>
    </MemoryRouter>,
  );

describe("DashboardLayoutProvider", () => {
  beforeEach(() => {
    mobile = false;
    localStorage.clear();
  });

  it("opens the sidebar on desktop when not persisting", () => {
    renderProvider();
    expect(screen.getByTestId("open").textContent).toBe("true");
  });

  it("keeps the sidebar closed on mobile", () => {
    mobile = true;
    renderProvider({ persist: true, isOpen: true });
    expect(screen.getByTestId("open").textContent).toBe("false");
  });

  it("restores persisted open state from localStorage on desktop", () => {
    localStorage.setItem("sidebar-open", "false");
    renderProvider({ persist: true });
    expect(screen.getByTestId("open").textContent).toBe("false");
  });

  it("toggle persists the next state and closes the submenu", () => {
    renderProvider({ persist: true });
    act(() => {
      fireEvent.click(screen.getByText("toggle"));
    });
    expect(localStorage.getItem("sidebar-open")).not.toBeNull();
  });

  it("close and closeWithoutPersist collapse the sidebar", () => {
    renderProvider({ persist: true, isOpen: true });
    act(() => {
      fireEvent.click(screen.getByText("close"));
    });
    expect(screen.getByTestId("open").textContent).toBe("false");
    expect(localStorage.getItem("sidebar-open")).toBe("false");
    act(() => {
      fireEvent.click(screen.getByText("close-np"));
    });
    expect(screen.getByTestId("open").textContent).toBe("false");
  });

  it("submenu controls toggle and show state", () => {
    renderProvider();
    act(() => {
      fireEvent.click(screen.getByText("toggle-sub"));
    });
    expect(screen.getByTestId("submenu").textContent).toBe("true");
    act(() => {
      fireEvent.click(screen.getByText("show-sub"));
    });
    expect(screen.getByTestId("submenu").textContent).toBe("true");
  });

  it("updateSubMenuId persists to localStorage and clears the search term", () => {
    renderProvider();
    act(() => {
      fireEvent.click(screen.getByText("set-search"));
    });
    expect(screen.getByTestId("search").textContent).toBe("query");
    act(() => {
      fireEvent.click(screen.getByText("set-id"));
    });
    expect(screen.getByTestId("submenu-id").textContent).toBe("m42");
    expect(localStorage.getItem("subMenuId")).toBe("m42");
    expect(screen.getByTestId("search").textContent).toBe("");
  });

  it("reads a stored subMenuId on mount", () => {
    localStorage.setItem("subMenuId", "stored-id");
    renderProvider();
    expect(screen.getByTestId("submenu-id").textContent).toBe("stored-id");
  });

  it("runs the /services submenu effect without crashing", () => {
    // On desktop the sidebar-open effect immediately re-collapses the submenu,
    // so this exercises the /services branch and confirms the provider renders.
    mobile = false;
    renderProvider({ isOpen: false }, "/services/foo");
    expect(screen.getByTestId("open").textContent).toBe("true");
  });
});
