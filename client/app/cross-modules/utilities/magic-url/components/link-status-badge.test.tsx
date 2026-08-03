import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { LinkStatusBadge } from "./link-status-badge";

describe("LinkStatusBadge", () => {
  it("maps known status codes to friendly labels", () => {
    const { rerender } = render(<LinkStatusBadge status="Link_Disabled" />);
    expect(screen.getByText("Disabled")).toBeInTheDocument();

    rerender(<LinkStatusBadge status="Link_Action_Limit_Exceeded" />);
    expect(screen.getByText("Limit Exceeded")).toBeInTheDocument();
  });

  it("renders the raw status when it is not in the map", () => {
    render(<LinkStatusBadge status="Weird" />);
    expect(screen.getByText("Weird")).toBeInTheDocument();
  });

  it("renders the active label", () => {
    render(<LinkStatusBadge status="Active" />);
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("renders the expired label", () => {
    render(<LinkStatusBadge status="Expired" />);
    expect(screen.getByText("Expired")).toBeInTheDocument();
  });
});
