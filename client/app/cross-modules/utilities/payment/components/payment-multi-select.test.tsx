import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { PaymentMultiSelect } from "./payment-multi-select";

const renderSelect = (
  props: Partial<React.ComponentProps<typeof PaymentMultiSelect>> = {},
) => {
  const onChange = vi.fn();
  render(
    <PaymentMultiSelect
      label="Provider"
      values={[]}
      options={["ADYEN-ONLINE", "STRIPE"]}
      emptyLabel="All providers"
      onChange={onChange}
      {...props}
    />,
  );
  return onChange;
};

const openMenu = () => fireEvent.click(screen.getByRole("button"));

describe("PaymentMultiSelect", () => {
  it("should show the empty label when nothing is selected", () => {
    renderSelect();

    expect(screen.getByText("All providers")).toBeTruthy();
  });

  it("should name the single selected value", () => {
    renderSelect({ values: ["STRIPE"] });

    expect(screen.getByText("STRIPE")).toBeTruthy();
  });

  it("should summarise once more than one value is selected", () => {
    renderSelect({ values: ["STRIPE", "ADYEN-ONLINE"] });

    expect(screen.getByText("2 selected")).toBeTruthy();
  });

  it("should add an option that is picked", () => {
    const onChange = renderSelect();
    openMenu();

    fireEvent.click(screen.getByLabelText("Select STRIPE"));

    expect(onChange).toHaveBeenCalledWith(["STRIPE"]);
  });

  it("should remove an option that is picked again", () => {
    const onChange = renderSelect({ values: ["STRIPE", "ADYEN-ONLINE"] });
    openMenu();

    fireEvent.click(screen.getByLabelText("Select STRIPE"));

    expect(onChange).toHaveBeenCalledWith(["ADYEN-ONLINE"]);
  });

  it("should list a selected value that is not among the known options", () => {
    // A filter restored from the URL can name a provider this build does not know.
    renderSelect({ values: ["LEGACY-PROVIDER"] });
    openMenu();

    expect(screen.getByLabelText("Select LEGACY-PROVIDER")).toBeTruthy();
  });

  it("should not list a known option twice when it is also selected", () => {
    renderSelect({ values: ["STRIPE"] });
    openMenu();

    expect(screen.getAllByLabelText("Select STRIPE")).toHaveLength(1);
  });

  it("should offer no free-text entry by default", () => {
    renderSelect();
    openMenu();

    expect(screen.queryByPlaceholderText("Add provider name")).toBeNull();
  });

  it("should add a custom value in upper case", () => {
    const onChange = renderSelect({ allowCustomValue: true });
    openMenu();

    fireEvent.change(screen.getByPlaceholderText("Add provider name"), {
      target: { value: " custom-provider " },
    });
    fireEvent.click(screen.getByRole("button", { name: "Add provider" }));

    expect(onChange).toHaveBeenCalledWith(["CUSTOM-PROVIDER"]);
  });

  it("should add a custom value on Enter without submitting a form", () => {
    const onChange = renderSelect({ allowCustomValue: true });
    openMenu();
    const input = screen.getByPlaceholderText("Add provider name");

    fireEvent.change(input, { target: { value: "custom" } });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(onChange).toHaveBeenCalledWith(["CUSTOM"]);
  });

  it("should ignore an empty custom value", () => {
    const onChange = renderSelect({ allowCustomValue: true });
    openMenu();

    fireEvent.change(screen.getByPlaceholderText("Add provider name"), {
      target: { value: "   " },
    });
    fireEvent.click(screen.getByRole("button", { name: "Add provider" }));

    expect(onChange).not.toHaveBeenCalled();
  });

  it("should ignore a custom value that is already selected", () => {
    const onChange = renderSelect({
      values: ["STRIPE"],
      allowCustomValue: true,
    });
    openMenu();

    fireEvent.change(screen.getByPlaceholderText("Add provider name"), {
      target: { value: "stripe" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Add provider" }));

    expect(onChange).not.toHaveBeenCalled();
  });

  it("should stop accepting custom values at twenty", () => {
    const onChange = renderSelect({
      values: Array.from({ length: 20 }, (_, index) => `P${index}`),
      allowCustomValue: true,
    });
    openMenu();

    fireEvent.change(screen.getByPlaceholderText("Add provider name"), {
      target: { value: "one-too-many" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Add provider" }));

    expect(onChange).not.toHaveBeenCalled();
  });

  it("should clear the entry box after adding", () => {
    renderSelect({ allowCustomValue: true });
    openMenu();
    const input = screen.getByPlaceholderText(
      "Add provider name",
    ) as HTMLInputElement;

    fireEvent.change(input, { target: { value: "custom" } });
    fireEvent.click(screen.getByRole("button", { name: "Add provider" }));

    expect(input.value).toBe("");
  });
});
