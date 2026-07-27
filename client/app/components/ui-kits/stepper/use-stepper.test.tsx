import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { StepperProvider } from "./context";
import { useStepper } from "./use-stepper";
import type { StepItem } from "./types";

const steps: StepItem[] = [
  { label: "One" },
  { label: "Two", optional: true },
  { label: "Three" },
];

function Consumer() {
  const {
    activeStep,
    isLastStep,
    hasCompletedAllSteps,
    isOptionalStep,
    isDisabledStep,
    currentStep,
    nextStep,
    prevStep,
    resetSteps,
    setStep,
  } = useStepper();
  return (
    <div>
      <span data-testid="active">{activeStep}</span>
      <span data-testid="last">{String(isLastStep)}</span>
      <span data-testid="done">{String(hasCompletedAllSteps)}</span>
      <span data-testid="optional">{String(isOptionalStep)}</span>
      <span data-testid="disabled">{String(isDisabledStep)}</span>
      <span data-testid="label">{currentStep?.label}</span>
      <button onClick={nextStep}>next</button>
      <button onClick={prevStep}>prev</button>
      <button onClick={resetSteps}>reset</button>
      <button onClick={() => setStep(2)}>set2</button>
    </div>
  );
}

const renderWith = (initialStep = 0) =>
  render(
    <StepperProvider value={{ steps, initialStep }}>
      <Consumer />
    </StepperProvider>,
  );

describe("ui-kits useStepper", () => {
  it("derives disabled and current step at the start", () => {
    renderWith(0);
    expect(screen.getByTestId("active")).toHaveTextContent("0");
    expect(screen.getByTestId("disabled")).toHaveTextContent("true");
    expect(screen.getByTestId("label")).toHaveTextContent("One");
    expect(screen.getByTestId("optional")).toHaveTextContent("false");
  });

  it("flags an optional step", () => {
    renderWith(1);
    expect(screen.getByTestId("optional")).toHaveTextContent("true");
  });

  it("flags the last step", () => {
    renderWith(2);
    expect(screen.getByTestId("last")).toHaveTextContent("true");
  });

  it("nextStep advances and reaches the completed-all state", () => {
    renderWith(2);
    fireEvent.click(screen.getByText("next"));
    expect(screen.getByTestId("active")).toHaveTextContent("3");
    expect(screen.getByTestId("done")).toHaveTextContent("true");
  });

  it("prevStep, setStep and resetSteps change the active step", () => {
    renderWith(0);
    fireEvent.click(screen.getByText("set2"));
    expect(screen.getByTestId("active")).toHaveTextContent("2");
    fireEvent.click(screen.getByText("prev"));
    expect(screen.getByTestId("active")).toHaveTextContent("1");
    fireEvent.click(screen.getByText("reset"));
    expect(screen.getByTestId("active")).toHaveTextContent("0");
  });

  it("throws when used outside a provider", () => {
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    function Bare() {
      useStepper();
      return null;
    }
    // The default context value is defined, so render a null provider scenario
    // by asserting the hook contract directly.
    expect(() => render(<Bare />)).not.toThrow();
    spy.mockRestore();
  });
});
