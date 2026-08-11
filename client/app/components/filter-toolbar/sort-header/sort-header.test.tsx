import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, renderHook, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NuqsTestingAdapter } from "nuqs/adapters/testing";
import { SortHeader, useSortQueryParams } from "./sort-header";

describe("SortHeader", () => {
  const renderHeader = (
    value = { property: "", isDescending: false },
    onChange = vi.fn(),
  ) => {
    render(<SortHeader id="name" label="Name" value={value} onChange={onChange} />);
    return { onChange, control: screen.getByRole("button", { name: /Name/ }) };
  };

  it("renders the label", () => {
    renderHeader();
    expect(screen.getByText("Name")).toBeInTheDocument();
  });

  it("sorts ascending on the first click", () => {
    const { onChange, control } = renderHeader();
    fireEvent.click(control);
    expect(onChange).toHaveBeenCalledWith({ property: "name", isDescending: false });
  });

  it("flips the direction when the active column is clicked again", () => {
    const { onChange, control } = renderHeader({
      property: "name",
      isDescending: false,
    });
    fireEvent.click(control);
    expect(onChange).toHaveBeenCalledWith({ property: "name", isDescending: true });
  });

  it("starts ascending when a different column was active", () => {
    const { onChange, control } = renderHeader({
      property: "email",
      isDescending: true,
    });
    fireEvent.click(control);
    expect(onChange).toHaveBeenCalledWith({ property: "name", isDescending: false });
  });

  it("sorts from the keyboard alone", async () => {
    const user = userEvent.setup();
    const { onChange, control } = renderHeader();
    control.focus();
    expect(control).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(onChange).toHaveBeenCalledWith({ property: "name", isDescending: false });

    onChange.mockClear();
    await user.keyboard("[Space]");
    expect(onChange).toHaveBeenCalledWith({ property: "name", isDescending: false });
  });
});

describe("useSortQueryParams", () => {
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <NuqsTestingAdapter>{children}</NuqsTestingAdapter>
  );

  it("starts from the supplied initial value", () => {
    const { result } = renderHook(
      () => useSortQueryParams({ initial: { property: "name", isDescending: true } }),
      { wrapper },
    );
    expect(result.current.sortQueryParams).toEqual({
      property: "name",
      isDescending: true,
    });
  });

  it("writes and then resets the sort query params", () => {
    const { result } = renderHook(() => useSortQueryParams({}), { wrapper });

    act(() => {
      result.current.setSortQueryParams({ property: "email", isDescending: true });
    });
    expect(result.current.sortQueryParams).toEqual({
      property: "email",
      isDescending: true,
    });

    act(() => {
      result.current.reset();
    });
    expect(result.current.sortQueryParams).toEqual({
      property: "",
      isDescending: false,
    });
  });
});
