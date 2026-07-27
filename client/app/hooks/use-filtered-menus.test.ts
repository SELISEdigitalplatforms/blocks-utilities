import { renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Menu } from "@/models/menu-models";
import { useFilteredMenus } from "./use-filtered-menus";

let pathname = "/app/dashboard";
vi.mock("react-router-dom", () => ({
  useLocation: () => ({ pathname }),
}));

const menu = (id: string, extra: Partial<Menu> = {}): Menu =>
  ({ id, type: "menu", name: id, path: `/${id}`, ...extra }) as Menu;
const sep = (id: string): Menu => ({ id, type: "separator" }) as unknown as Menu;

describe("useFilteredMenus", () => {
  afterEach(() => {
    pathname = "/app/dashboard";
    vi.unstubAllEnvs();
  });

  it("hides project-scoped menus when not on a project route", () => {
    pathname = "/app/dashboard";
    const { result } = renderHook(() =>
      useFilteredMenus([menu("overview-project"), menu("environments")]),
    );
    const ids = result.current.map((m) => m.id);
    expect(ids).toContain("overview-project");
    expect(ids).not.toContain("environments");
  });

  it("hides non-project menus when on a project route", () => {
    pathname = "/app/project/tg-1/environments";
    const { result } = renderHook(() =>
      useFilteredMenus([menu("overview-project"), menu("environments")]),
    );
    const ids = result.current.map((m) => m.id);
    expect(ids).toContain("environments");
    expect(ids).not.toContain("overview-project");
  });

  it("drops disabled items", () => {
    const { result } = renderHook(() =>
      useFilteredMenus([menu("magic-url", { disabled: true })]),
    );
    expect(result.current).toHaveLength(0);
  });

  it("respects the blocked-menu env list", () => {
    vi.stubEnv("BLOCKS_BLOCKED_MENU", JSON.stringify(["magic-url"]));
    const { result } = renderHook(() =>
      useFilteredMenus([menu("magic-url"), menu("apps")]),
    );
    expect(result.current.map((m) => m.id)).toEqual(["apps"]);
  });

  it("tolerates malformed blocked-menu env", () => {
    vi.stubEnv("BLOCKS_BLOCKED_MENU", "not-json");
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    const { result } = renderHook(() => useFilteredMenus([menu("apps")]));
    expect(result.current.map((m) => m.id)).toEqual(["apps"]);
    spy.mockRestore();
  });

  it("removes separators left dangling between other separators", () => {
    const { result } = renderHook(() =>
      useFilteredMenus([
        menu("apps"),
        sep("separator-a"),
        sep("separator-b"),
        menu("more"),
      ]),
    );
    const seps = result.current.filter((m) => m.type === "separator");
    expect(seps.length).toBeLessThan(2);
  });
});
