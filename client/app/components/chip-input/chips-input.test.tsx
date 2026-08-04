import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, renderHook } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import {
  ChipsInput,
  ChipsInputField,
  ChipsInputList,
  useChipsContext,
} from "./chips-input";

function Harness({
  validatorRegex,
  customValidator,
  errorMessage,
  initial = [],
}: {
  validatorRegex?: RegExp;
  customValidator?: (v: string) => boolean;
  errorMessage?: string;
  initial?: string[];
}) {
  const [value, setValue] = useState<string[]>(initial);
  return (
    <ChipsInput
      value={value}
      onChange={setValue}
      validatorRegex={validatorRegex}
      customValidator={customValidator}
      validatorRegexErrorMessage={errorMessage}
    >
      <ChipsInputList />
      <ChipsInputField />
    </ChipsInput>
  );
}

describe("ChipsInput", () => {
  it("throws when a sub-component is used outside the provider", () => {
    expect(() => renderHook(() => useChipsContext())).toThrow(
      /must be used within/,
    );
  });

  it("adds a chip on Enter and clears the input", () => {
    render(<Harness />);
    const input = screen.getByPlaceholderText("Type and press enter");
    fireEvent.change(input, { target: { value: "alpha" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(screen.getByText("alpha")).toBeInTheDocument();
    expect(input).toHaveValue("");
  });

  it("removes a chip when its remove control is clicked", () => {
    render(<Harness initial={["one", "two"]} />);
    fireEvent.click(screen.getByRole("button", { name: "Remove one" }));
    expect(screen.queryByText("one")).not.toBeInTheDocument();
    expect(screen.getByText("two")).toBeInTheDocument();
  });

  it("removes a chip with the keyboard alone", async () => {
    const user = userEvent.setup();
    render(<Harness initial={["one", "two"]} />);
    const remove = screen.getByRole("button", { name: "Remove one" });
    remove.focus();
    expect(remove).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(screen.queryByText("one")).not.toBeInTheDocument();
    expect(screen.getByText("two")).toBeInTheDocument();
  });

  it("removes a chip with the Space key", async () => {
    const user = userEvent.setup();
    render(<Harness initial={["one", "two"]} />);
    screen.getByRole("button", { name: "Remove two" }).focus();
    await user.keyboard("[Space]");
    expect(screen.queryByText("two")).not.toBeInTheDocument();
    expect(screen.getByText("one")).toBeInTheDocument();
  });

  it("shows a validation error from a regex and blocks adding", () => {
    render(
      <Harness validatorRegex={/^\d+$/} errorMessage="Digits only" />,
    );
    const input = screen.getByPlaceholderText("Type and press enter");
    fireEvent.change(input, { target: { value: "abc" } });
    expect(screen.getByText("Digits only")).toBeInTheDocument();
    fireEvent.keyDown(input, { key: "Enter" });
    expect(screen.queryByText("abc")).not.toBeInTheDocument();
  });

  it("clears the error once the value becomes valid", () => {
    render(<Harness validatorRegex={/^\d+$/} errorMessage="Digits only" />);
    const input = screen.getByPlaceholderText("Type and press enter");
    fireEvent.change(input, { target: { value: "abc" } });
    expect(screen.getByText("Digits only")).toBeInTheDocument();
    fireEvent.change(input, { target: { value: "123" } });
    expect(screen.queryByText("Digits only")).not.toBeInTheDocument();
  });

  it("supports a custom validator", () => {
    const validator = vi.fn((v: string) => v.length > 2);
    render(<Harness customValidator={validator} errorMessage="Too short" />);
    const input = screen.getByPlaceholderText("Type and press enter");
    fireEvent.change(input, { target: { value: "a" } });
    expect(screen.getByText("Too short")).toBeInTheDocument();
    fireEvent.change(input, { target: { value: "abcd" } });
    expect(screen.queryByText("Too short")).not.toBeInTheDocument();
  });

  it("clears the error when the field is emptied", () => {
    render(<Harness validatorRegex={/^\d+$/} errorMessage="Digits only" />);
    const input = screen.getByPlaceholderText("Type and press enter");
    fireEvent.change(input, { target: { value: "x" } });
    expect(screen.getByText("Digits only")).toBeInTheDocument();
    fireEvent.change(input, { target: { value: "" } });
    expect(screen.queryByText("Digits only")).not.toBeInTheDocument();
  });
});
