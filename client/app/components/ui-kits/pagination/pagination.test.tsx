import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { Pagination } from "./pagination";

describe("Pagination", () => {
  it("renders the current page and disables previous on the first page", () => {
    const onChange = vi.fn();
    render(
      <Pagination page={0} onChange={onChange} totalCount={30} pageSize={10} />,
    );
    expect(screen.getByText(/Page 1 of 3/)).toBeInTheDocument();
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[0]);
    fireEvent.click(buttons[1]);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("navigates next and to the last page", () => {
    const onChange = vi.fn();
    render(
      <Pagination page={0} onChange={onChange} totalCount={30} pageSize={10} />,
    );
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[2]);
    expect(onChange).toHaveBeenCalledWith(1);
    fireEvent.click(buttons[3]);
    expect(onChange).toHaveBeenCalledWith(2);
  });

  it("renders the page-size selector when options are supplied", () => {
    const onChange = vi.fn();
    render(
      <Pagination
        page={1}
        onChange={onChange}
        totalCount={30}
        pageSize={10}
        pageSizeOptions={[10, 20]}
        onPageSizeChange={vi.fn()}
      />,
    );
    expect(screen.getByText("Rows per page")).toBeInTheDocument();
  });

  it("goes to the previous page from a later page", () => {
    const onChange = vi.fn();
    render(
      <Pagination page={2} onChange={onChange} totalCount={30} pageSize={10} />,
    );
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[0]);
    expect(onChange).toHaveBeenCalledWith(0);
    fireEvent.click(buttons[1]);
    expect(onChange).toHaveBeenCalledWith(1);
  });
});
