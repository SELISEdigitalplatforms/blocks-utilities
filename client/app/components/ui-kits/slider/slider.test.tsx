import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { Slider } from "./slider";

describe("Slider", () => {
  it("renders a single thumb for a single controlled value", () => {
    render(<Slider value={[40]} min={0} max={100} />);
    expect(screen.getAllByRole("slider")).toHaveLength(1);
  });

  it("renders two thumbs for a range value", () => {
    render(<Slider value={[20, 80]} />);
    expect(screen.getAllByRole("slider")).toHaveLength(2);
  });

  it("renders a thumb when no value is given", () => {
    render(<Slider />);
    expect(screen.getAllByRole("slider").length).toBeGreaterThanOrEqual(1);
  });

  it("derives thumbs from a defaultValue", () => {
    render(<Slider defaultValue={[10]} />);
    expect(screen.getAllByRole("slider")).toHaveLength(1);
  });
});
