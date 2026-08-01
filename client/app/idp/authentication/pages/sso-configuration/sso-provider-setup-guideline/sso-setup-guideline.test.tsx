import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { SSOSetupGuideLine } from "./sso-setup-guideline";

const steps = [
  { id: "1", description: <span>Step one details</span> },
  { id: "2", description: <span>Step two details</span> },
  { id: "3", description: <span>Step three details</span> },
];

describe("SSOSetupGuideLine", () => {
  it("shows the first step and disables the previous button", () => {
    render(<SSOSetupGuideLine steps={steps} />);
    expect(screen.getByText("Step one details")).toBeInTheDocument();
    const buttons = screen.getAllByRole("button");
    // Previous is the first button and starts disabled.
    expect(buttons[0]).toBeDisabled();
    expect(buttons[1]).toBeEnabled();
  });

  it("advances and goes back through the steps", async () => {
    const user = userEvent.setup();
    render(<SSOSetupGuideLine steps={steps} />);
    const [prev, next] = screen.getAllByRole("button");

    await user.click(next);
    expect(screen.getByText("Step two details")).toBeInTheDocument();

    await user.click(next);
    expect(screen.getByText("Step three details")).toBeInTheDocument();
    // Next is disabled on the last step.
    expect(next).toBeDisabled();

    await user.click(prev);
    expect(screen.getByText("Step two details")).toBeInTheDocument();
  });

  it("shows full progress for a single-step guideline", () => {
    render(<SSOSetupGuideLine steps={[steps[0]]} />);
    expect(screen.getByText("Step one details")).toBeInTheDocument();
  });
});
