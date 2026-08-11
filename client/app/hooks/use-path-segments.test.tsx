import React from "react";
import { renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { MemoryRouter } from "react-router";
import { BreadcrumbProvider } from "@/contexts/breadcrumb-context";
import useRoutePathSegments, { usePreviousPath } from "./use-path-segments";

const wrapperFor = (initialPath: string) =>
  function Wrapper({ children }: { children: React.ReactNode }) {
    return (
      <MemoryRouter initialEntries={[initialPath]}>
        <BreadcrumbProvider>{children}</BreadcrumbProvider>
      </MemoryRouter>
    );
  };

describe("useRoutePathSegments", () => {
  it("builds cumulative breadcrumbs from the path with formatted labels", () => {
    const { result } = renderHook(() => useRoutePathSegments(), {
      wrapper: wrapperFor("/app/user-management"),
    });
    expect(result.current).toEqual([
      { href: "/app", label: "App" },
      { href: "/app/user-management", label: "User Management" },
    ]);
  });

  it("applies a custom breadcrumb title", () => {
    const { result } = renderHook(() => useRoutePathSegments(), {
      wrapper: wrapperFor("/magic-url"),
    });
    expect(result.current.find((b) => b.href === "/magic-url")?.label).toBe(
      "Magic URL",
    );
  });

  it("maps the scoped Magic URL List breadcrumb to the list page", () => {
    const { result } = renderHook(() => useRoutePathSegments(), {
      wrapper: wrapperFor(
        "/app/project-1/magic-url/details/magic-url-1",
      ),
    });

    expect(result.current.find((breadcrumb) => breadcrumb.label === "List"))
      .toEqual({
        href: "/app/project-1/magic-url",
        label: "List",
      });
  });

  it("returns an empty array at the root", () => {
    const { result } = renderHook(() => useRoutePathSegments(), {
      wrapper: wrapperFor("/"),
    });
    expect(result.current).toEqual([]);
  });
});

describe("usePreviousPath", () => {
  it("starts with no previous path", () => {
    const { result } = renderHook(() => usePreviousPath(), {
      wrapper: wrapperFor("/a"),
    });
    expect(result.current).toBeNull();
  });
});
