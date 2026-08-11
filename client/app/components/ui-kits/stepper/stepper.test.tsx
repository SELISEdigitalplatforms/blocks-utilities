import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, createEvent } from "@testing-library/react";
import { Stepper, Step } from "./index";
import type { StepItem } from "./types";
import { Home } from "lucide-react";

const steps: StepItem[] = [
  { label: "First", description: "step one" },
  { label: "Second", description: "step two" },
  { label: "Third", description: "step three" },
];

const renderStepper = (props: Record<string, unknown> = {}) =>
  render(
    <Stepper initialStep={0} steps={steps} {...props}>
      {steps.map((s) => (
        <Step key={s.label} label={s.label} description={s.description}>
          <div>content-{s.label}</div>
        </Step>
      ))}
      <div>footer</div>
    </Stepper>,
  );

describe("Stepper", () => {
  it("renders a horizontal stepper with labels and the footer", () => {
    renderStepper();
    expect(screen.getByText("First")).toBeInTheDocument();
    expect(screen.getByText("Second")).toBeInTheDocument();
    expect(screen.getByText("footer")).toBeInTheDocument();
    // The step index label shows the 1-based number when no icon.
    expect(screen.getByText("1")).toBeInTheDocument();
  });

  it("shows completed check icons for steps before the active step", () => {
    const { container } = renderStepper({ initialStep: 2 });
    // lucide CheckIcon renders an svg with the lucide-check class.
    expect(container.querySelector(".lucide-check")).toBeTruthy();
  });

  it("renders in the vertical orientation with step content", () => {
    const { container } = renderStepper({
      orientation: "vertical",
      responsive: false,
    });
    expect(
      container.querySelector(".stepper__vertical-step"),
    ).toBeInTheDocument();
    expect(screen.getByText("content-First")).toBeInTheDocument();
  });

  it("renders the line variant without step buttons", () => {
    const { container } = renderStepper({ variant: "line" });
    expect(
      container.querySelector(".stepper__step-button-container"),
    ).toBeNull();
  });

  it("renders the circle-alt variant", () => {
    const { container } = renderStepper({ variant: "circle-alt" });
    expect(container.querySelector(".stepper__horizontal-step")).toBeTruthy();
  });

  it("shows a spinner on the current step when loading", () => {
    const { container } = renderStepper({ state: "loading" });
    expect(container.querySelector(".animate-spin")).toBeTruthy();
  });

  it("shows an error icon on the current step when in the error state", () => {
    const { container } = renderStepper({ state: "error" });
    expect(container.querySelector(".lucide-x")).toBeTruthy();
  });

  it("renders a custom step icon", () => {
    render(
      <Stepper initialStep={0} steps={[{ label: "One" }]}>
        <Step label="One" icon={Home} />
      </Stepper>,
    );
    expect(document.querySelector(".lucide-house, .lucide-home")).toBeTruthy();
  });

  it("invokes onClickStep when a step is clicked", () => {
    const onClickStep = vi.fn();
    renderStepper({ onClickStep });
    fireEvent.click(screen.getByText("First"));
    expect(onClickStep).toHaveBeenCalled();
  });

  it("invokes onClickStep when Enter is pressed on a horizontal step", () => {
    const onClickStep = vi.fn();
    renderStepper({ onClickStep });
    fireEvent.keyDown(screen.getByRole("button", { name: /First/ }), {
      key: "Enter",
    });
    expect(onClickStep).toHaveBeenCalledTimes(1);
  });

  it("invokes onClickStep on Space and prevents the page from scrolling", () => {
    const onClickStep = vi.fn();
    renderStepper({ onClickStep });
    const step = screen.getByRole("button", { name: /First/ });
    const event = createEvent.keyDown(step, { key: " " });
    fireEvent(step, event);
    expect(onClickStep).toHaveBeenCalledTimes(1);
    expect(event.defaultPrevented).toBe(true);
  });

  it("ignores other keys on a horizontal step", () => {
    const onClickStep = vi.fn();
    renderStepper({ onClickStep });
    fireEvent.keyDown(screen.getByRole("button", { name: /First/ }), {
      key: "Tab",
    });
    expect(onClickStep).not.toHaveBeenCalled();
  });

  it("does not fire the horizontal step action for keys raised by its inner step button", () => {
    const onClickStep = vi.fn();
    renderStepper({ onClickStep });
    fireEvent.keyDown(screen.getByRole("button", { name: "1" }), {
      key: "Enter",
    });
    expect(onClickStep).not.toHaveBeenCalled();
  });

  it("invokes onClickStep from the keyboard on a vertical step", () => {
    const onClickStep = vi.fn();
    renderStepper({ orientation: "vertical", responsive: false, onClickStep });
    const step = screen.getByRole("button", { name: /First/ });
    fireEvent.keyDown(step, { key: "Enter" });
    expect(onClickStep).toHaveBeenCalledTimes(1);

    const event = createEvent.keyDown(step, { key: " " });
    fireEvent(step, event);
    expect(onClickStep).toHaveBeenCalledTimes(2);
    expect(event.defaultPrevented).toBe(true);

    fireEvent.keyDown(step, { key: "Tab" });
    expect(onClickStep).toHaveBeenCalledTimes(2);
  });

  it("does not fire the vertical step action for keys raised by its inner step button", () => {
    const onClickStep = vi.fn();
    renderStepper({ orientation: "vertical", responsive: false, onClickStep });
    fireEvent.keyDown(screen.getByRole("button", { name: "1" }), {
      key: "Enter",
    });
    expect(onClickStep).not.toHaveBeenCalled();
  });

  it("throws when given a non-element child", () => {
    expect(() =>
      render(
        <Stepper initialStep={0} steps={steps}>
          {"plain text" as unknown as React.ReactElement}
        </Stepper>,
      ),
    ).toThrow("Stepper children must be valid React elements.");
  });
});
