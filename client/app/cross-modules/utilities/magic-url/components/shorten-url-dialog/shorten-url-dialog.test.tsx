import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { ShortenUrlDialog } from "./shorten-url-dialog";

describe("ShortenUrlDialog", () => {
  let logSpy: ReturnType<typeof vi.spyOn>;
  beforeEach(() => {
    logSpy = vi.spyOn(console, "error").mockImplementation(() => {});
  });
  afterEach(() => {
    logSpy.mockRestore();
  });

  it("renders the dialog fields when open", () => {
    render(<ShortenUrlDialog open onOpenChange={() => {}} />);
    expect(screen.getByText("Short URL")).toBeInTheDocument();
    expect(screen.getByLabelText("Enter URL")).toBeInTheDocument();
    expect(screen.getByText("Auto Generated")).toBeInTheDocument();
  });

  it("reveals the alias input and validates its length", () => {
    render(<ShortenUrlDialog open onOpenChange={() => {}} />);
    fireEvent.click(screen.getByLabelText("Set Alias"));
    const aliasInput = screen.getByPlaceholderText("Enter alias");
    fireEvent.change(aliasInput, { target: { value: "abc" } });
    expect(screen.getByText("min 5 characters")).toBeInTheDocument();
    expect(screen.queryByText("Alias is available")).not.toBeInTheDocument();
    fireEvent.change(aliasInput, { target: { value: "abcdef" } });
    expect(screen.getByText("Alias is available")).toBeInTheDocument();
  });

  it("reveals the usage limit input when toggled", () => {
    render(<ShortenUrlDialog open onOpenChange={() => {}} />);
    fireEvent.click(screen.getByLabelText("Set Usage Limit"));
    expect(screen.getByPlaceholderText("Enter usage limit")).toBeInTheDocument();
  });

  it("reveals the expiry date picker when toggled", () => {
    render(<ShortenUrlDialog open onOpenChange={() => {}} />);
    fireEvent.click(screen.getByLabelText("Set Auto Expiry Date"));
    expect(screen.getByText("Pick a date")).toBeInTheDocument();
  });

  it("logs the payload on Shorten", () => {
    render(<ShortenUrlDialog open onOpenChange={() => {}} />);
    fireEvent.change(screen.getByLabelText("Enter URL"), {
      target: { value: "https://example.com" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Shorten" }));
    expect(logSpy).toHaveBeenCalledWith(
      expect.objectContaining({ url: "https://example.com" }),
    );
  });

  it("resets and closes on Cancel", () => {
    const onOpenChange = vi.fn();
    render(<ShortenUrlDialog open onOpenChange={onOpenChange} />);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
