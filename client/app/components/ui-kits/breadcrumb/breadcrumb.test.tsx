import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import {
  Breadcrumb,
  BreadcrumbList,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbPage,
  BreadcrumbSeparator,
  BreadcrumbEllipsis,
} from "./breadcrumb";

describe("Breadcrumb", () => {
  it("renders a full breadcrumb trail with links, page and separators", () => {
    render(
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink href="/home">Home</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbEllipsis />
          </BreadcrumbItem>
          <BreadcrumbSeparator>/</BreadcrumbSeparator>
          <BreadcrumbItem>
            <BreadcrumbPage>Current</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>,
    );

    expect(screen.getByRole("navigation", { name: "breadcrumb" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Home" })).toHaveAttribute("href", "/home");
    expect(screen.getByText("Current")).toHaveAttribute("aria-current", "page");
    expect(screen.getByText("More")).toBeInTheDocument();
  });

  it("renders a link as a child element when asChild is set", () => {
    render(
      <BreadcrumbLink asChild>
        <button type="button">Trigger</button>
      </BreadcrumbLink>,
    );
    expect(screen.getByRole("button", { name: "Trigger" })).toBeInTheDocument();
  });
});
