import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CopyToClipboardButton } from "./copy-to-clipboard-button";

function setClipboard(value: unknown): void {
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    writable: true,
    value,
  });
}

function setSecureContext(value: boolean): void {
  Object.defineProperty(window, "isSecureContext", {
    configurable: true,
    writable: true,
    value,
  });
}

describe("CopyToClipboardButton", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders its children alongside the copy button", () => {
    render(
      <CopyToClipboardButton textToCopy="hello">
        <span>the value</span>
      </CopyToClipboardButton>,
    );
    expect(screen.getByText("the value")).toBeInTheDocument();
    expect(screen.getByRole("button")).toBeInTheDocument();
  });

  it("copies via the clipboard API in a secure context", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    const user = userEvent.setup();
    setClipboard({ writeText });
    setSecureContext(true);
    render(
      <CopyToClipboardButton textToCopy="secret-token">
        <span>x</span>
      </CopyToClipboardButton>,
    );

    await user.click(screen.getByRole("button"));
    expect(writeText).toHaveBeenCalledWith("secret-token");
  });

  it("falls back to execCommand outside a secure context", async () => {
    const user = userEvent.setup();
    setSecureContext(false);
    setClipboard(undefined);
    const execCommand = vi.fn().mockReturnValue(true);
    document.execCommand = execCommand as unknown as typeof document.execCommand;
    render(
      <CopyToClipboardButton textToCopy="fallback">
        <span>x</span>
      </CopyToClipboardButton>,
    );

    await user.click(screen.getByRole("button"));
    expect(execCommand).toHaveBeenCalledWith("copy");
  });

  it("logs an error when copying fails", async () => {
    const writeText = vi.fn().mockRejectedValue(new Error("nope"));
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    const user = userEvent.setup();
    setClipboard({ writeText });
    setSecureContext(true);
    render(
      <CopyToClipboardButton textToCopy="x">
        <span>x</span>
      </CopyToClipboardButton>,
    );

    await user.click(screen.getByRole("button"));
    await waitFor(() =>
      expect(errorSpy).toHaveBeenCalledWith("Failed to copy:", expect.any(Error)),
    );
    errorSpy.mockRestore();
  });
});
