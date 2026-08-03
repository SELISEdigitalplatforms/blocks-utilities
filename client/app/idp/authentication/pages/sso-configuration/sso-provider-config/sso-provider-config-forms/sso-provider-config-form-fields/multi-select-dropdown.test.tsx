import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useForm } from "react-hook-form";
import { Form, FormField, FormItem } from "@/components/ui-kits/form/form";
import { MultiSelectDropdown } from "./multi-select-dropdown";

const options = [
  { label: "Read", value: "read" },
  { label: "Write", value: "write" },
  { label: "Delete", value: "delete" },
];

// MultiSelectDropdown renders a FormControl, so it must live inside a FormField.
function Harness({
  value,
  onChange,
  disabled,
}: {
  value: string[];
  onChange: (v: string[]) => void;
  disabled?: boolean;
}) {
  const form = useForm({ defaultValues: { sel: value } });
  return (
    <Form {...form}>
      <FormField
        control={form.control}
        name="sel"
        render={() => (
          <FormItem>
            <MultiSelectDropdown
              options={options}
              value={value}
              onChange={onChange}
              disabled={disabled}
              placeholder="Pick some"
            />
          </FormItem>
        )}
      />
    </Form>
  );
}

describe("MultiSelectDropdown", () => {
  beforeEach(() => vi.clearAllMocks());

  it("shows the placeholder when nothing is selected", () => {
    render(<Harness value={[]} onChange={vi.fn()} />);
    expect(screen.getByText("Pick some")).toBeInTheDocument();
  });

  it("shows the selected labels joined together", () => {
    render(<Harness value={["read", "write"]} onChange={vi.fn()} />);
    expect(screen.getByText("Read, Write")).toBeInTheDocument();
  });

  it("adds a value when an option is toggled on", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness value={[]} onChange={onChange} />);

    await user.click(screen.getByRole("button"));
    await user.click(await screen.findByText("Write"));
    expect(onChange).toHaveBeenCalledWith(["write"]);
  });

  it("clears the selection via the clear action", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness value={["read"]} onChange={onChange} />);

    await user.click(screen.getByRole("button"));
    await user.click(await screen.findByText("Clear selection"));
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("does not open when disabled", async () => {
    const user = userEvent.setup();
    render(<Harness value={[]} onChange={vi.fn()} disabled />);
    await user.click(screen.getByRole("button"));
    expect(screen.queryByText("Read")).not.toBeInTheDocument();
  });
});
