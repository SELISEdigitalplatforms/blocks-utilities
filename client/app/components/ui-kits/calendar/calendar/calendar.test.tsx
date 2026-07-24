import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { Calendar } from "./calendar";

describe("Calendar", () => {
  it("renders a day grid for the given month", () => {
    render(<Calendar mode="single" month={new Date(2024, 0, 1)} />);
    expect(screen.getByRole("grid")).toBeInTheDocument();
    expect(screen.getByText("15")).toBeInTheDocument();
  });

  it("renders the previous and next navigation buttons", () => {
    render(<Calendar mode="single" month={new Date(2024, 0, 1)} />);
    expect(screen.getAllByRole("button").length).toBeGreaterThanOrEqual(2);
  });
});
