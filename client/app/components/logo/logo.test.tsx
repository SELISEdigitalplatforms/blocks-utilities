import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";

import { Logo } from "./index";

describe("Logo", () => {
  it("renders the provided src when given", () => {
    render(<Logo src="/custom.svg" alt="Custom" width={100} height={40} />);
    const img = screen.getByAltText("Custom");
    expect(img).toHaveAttribute("src", "/custom.svg");
    expect(img).toHaveAttribute("width", "100");
  });

  it("falls back to the themed logo asset when no src is given", () => {
    render(<Logo />);
    const img = screen.getByAltText("SELISE Logo");
    // Light theme (test stub) resolves to the black wordmark.
    expect(img.getAttribute("src")).toContain("utilities_logo_black.svg");
  });

  it("uses the icon variant asset", () => {
    render(<Logo variant="icon" />);
    const img = screen.getByAltText("SELISE Logo");
    expect(img.getAttribute("src")).toContain("Icon_Black.svg");
  });
});
