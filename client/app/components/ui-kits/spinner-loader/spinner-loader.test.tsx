import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import SpinnerLoader from "./spinner-loader";

describe("SpinnerLoader", () => {
  it("renders a status role with the loading label", () => {
    render(<SpinnerLoader />);
    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(screen.getByText("Loading...")).toBeInTheDocument();
  });
});
