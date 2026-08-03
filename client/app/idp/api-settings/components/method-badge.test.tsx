import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";

import { MethodBadge } from "./method-badge";

describe("MethodBadge", () => {
  it.each(["GET", "POST", "PUT", "PATCH", "DELETE"])(
    "renders a styled badge for %s",
    (method) => {
      render(<MethodBadge method={method} />);
      expect(screen.getByText(method)).toBeInTheDocument();
    },
  );

  it("uppercases the method and applies the fallback style for unknown verbs", () => {
    render(<MethodBadge method="trace" />);
    const badge = screen.getByText("TRACE");
    expect(badge).toBeInTheDocument();
    expect(badge.className).toContain("bg-muted");
  });
});
