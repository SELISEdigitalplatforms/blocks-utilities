import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

import PageBreadcrumb from "./breadcrumb";
import { BreadcrumbProvider } from "@/contexts/breadcrumb-context";

const renderAt = (path: string, ui: React.ReactElement) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <BreadcrumbProvider>{ui}</BreadcrumbProvider>
    </MemoryRouter>,
  );

describe("PageBreadcrumb", () => {
  it("renders a formatted breadcrumb for each path segment", () => {
    renderAt("/services/authentication", <PageBreadcrumb />);
    expect(screen.getByText("Authentication")).toBeInTheDocument();
  });

  it("prepends a parent breadcrumb when provided", () => {
    renderAt(
      "/services/authentication",
      <PageBreadcrumb parentBreadcrumb={{ href: "/home", label: "Home" }} />,
    );
    expect(screen.getByText("Home")).toBeInTheDocument();
    expect(screen.getByText("Authentication")).toBeInTheDocument();
  });

  it("shows only the last N breadcrumbs when breadcrumbIndex is set", () => {
    renderAt("/a/b/c", <PageBreadcrumb breadcrumbIndex={1} />);
    // Only the final segment survives the slice.
    expect(screen.getByText("C")).toBeInTheDocument();
    expect(screen.queryByText("A")).not.toBeInTheDocument();
  });
});
