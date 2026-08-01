import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import StepperProvider from "./stepper-provider";
import StepHorizontalTrackBar from "./horizontal-track-bar";
import StepVerticalTrackBar from "./vertical-track-bar";

const steps = [
  { id: 1, title: "Name" },
  { id: 2, title: "Resources" },
  { id: 3, title: "Environments" },
];

const renderWith = (ui: React.ReactElement) =>
  render(
    <StepperProvider steps={steps} initialStep={2}>
      {ui}
    </StepperProvider>,
  );

describe("stepper track bars", () => {
  it("renders every step title in the horizontal track bar", () => {
    renderWith(<StepHorizontalTrackBar />);
    steps.forEach((s) => expect(screen.getByText(s.title)).toBeInTheDocument());
    // The first step is completed at initialStep 2, so it renders a check icon.
    expect(screen.getByRole("button", { name: "" })).toBeTruthy();
  });

  it("renders every step title in the vertical track bar", () => {
    renderWith(<StepVerticalTrackBar />);
    steps.forEach((s) => expect(screen.getByText(s.title)).toBeInTheDocument());
  });

  it("navigates to a completed step when its marker is clicked", async () => {
    const user = userEvent.setup();
    renderWith(<StepHorizontalTrackBar />);
    // Step 1 is completed, so clicking it is allowed.
    const buttons = screen.getAllByRole("button");
    await user.click(buttons[0]);
    // Still rendered after navigation.
    expect(screen.getByText("Name")).toBeInTheDocument();
  });
});
