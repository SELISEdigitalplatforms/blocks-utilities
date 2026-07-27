import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import StepperProvider, { useStepper } from "./stepper-provider";
import type { Steps } from "./stepper-models";

const steps = [
  { label: "One" },
  { label: "Two" },
  { label: "Three" },
] as unknown as Steps;

function Consumer() {
  const {
    currentStep,
    nextStep,
    previousStep,
    goToStep,
    completedSteps,
    totalSteps,
    getSteps,
  } = useStepper();
  return (
    <div>
      <span data-testid="current">{currentStep}</span>
      <span data-testid="total">{totalSteps}</span>
      <span data-testid="completed">{completedSteps.join(",")}</span>
      <span data-testid="steps">{getSteps().length}</span>
      <button onClick={nextStep}>next</button>
      <button onClick={previousStep}>prev</button>
      <button onClick={() => goToStep(3)}>go3</button>
      <button onClick={() => goToStep(2)}>go2</button>
    </div>
  );
}

const renderProvider = (props = {}) =>
  render(
    <StepperProvider steps={steps} {...props}>
      <Consumer />
    </StepperProvider>,
  );

describe("StepperProvider", () => {
  it("exposes initial state", () => {
    renderProvider();
    expect(screen.getByTestId("current")).toHaveTextContent("1");
    expect(screen.getByTestId("total")).toHaveTextContent("3");
    expect(screen.getByTestId("steps")).toHaveTextContent("3");
  });

  it("advances and records completed steps", () => {
    renderProvider();
    fireEvent.click(screen.getByText("next"));
    expect(screen.getByTestId("current")).toHaveTextContent("2");
    expect(screen.getByTestId("completed")).toHaveTextContent("1");
  });

  it("does not advance past the last step", () => {
    renderProvider();
    fireEvent.click(screen.getByText("next"));
    fireEvent.click(screen.getByText("next"));
    fireEvent.click(screen.getByText("next"));
    expect(screen.getByTestId("current")).toHaveTextContent("3");
  });

  it("moves back with previousStep", () => {
    renderProvider();
    fireEvent.click(screen.getByText("next"));
    fireEvent.click(screen.getByText("prev"));
    expect(screen.getByTestId("current")).toHaveTextContent("1");
  });

  it("does not move before the first step", () => {
    renderProvider();
    fireEvent.click(screen.getByText("prev"));
    expect(screen.getByTestId("current")).toHaveTextContent("1");
  });

  it("goToStep blocks jumps to incomplete steps", () => {
    renderProvider();
    fireEvent.click(screen.getByText("go3"));
    expect(screen.getByTestId("current")).toHaveTextContent("1");
  });

  it("goToStep allows jumping to a reachable step", () => {
    renderProvider();
    fireEvent.click(screen.getByText("next")); // completes step 1
    fireEvent.click(screen.getByText("go2"));
    expect(screen.getByTestId("current")).toHaveTextContent("2");
  });

  it("respects an isStepValid guard on goToStep", () => {
    // Start on step 3 so steps 1 and 2 are already completed; goToStep(2)
    // would otherwise be reachable, so only the guard can block it.
    renderProvider({ isStepValid: () => false, initialStep: 3 });
    fireEvent.click(screen.getByText("go2"));
    expect(screen.getByTestId("current")).toHaveTextContent("3");
  });

  it("honors an initialStep", () => {
    renderProvider({ initialStep: 2 });
    expect(screen.getByTestId("current")).toHaveTextContent("2");
    expect(screen.getByTestId("completed")).toHaveTextContent("1");
  });

  it("throws when useStepper is used outside a provider", () => {
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    expect(() => render(<Consumer />)).toThrow(
      "useStepper must be used within a StepperProvider",
    );
    spy.mockRestore();
  });
});
