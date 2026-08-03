import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CopyableSnippet } from "./copyable-snippet";

function setClipboard(value: unknown): void {
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    writable: true,
    value,
  });
}

describe("CopyableSnippet", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("renders the code and copies it via the clipboard API on click", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    const user = userEvent.setup();
    setClipboard({ writeText });
    const { container } = render(
      <CopyableSnippet code="npm install" language="bash" isCopyable />,
    );

    // react-syntax-highlighter tokenizes the code into multiple spans, so the
    // text is asserted via the rendered <pre> content rather than an exact
    // getByText match.
    expect(container.querySelector("pre")?.textContent).toContain("npm");
    expect(container.querySelector("pre")?.textContent).toContain("install");

    await user.click(screen.getByRole("button", { name: "Copy code" }));
    expect(writeText).toHaveBeenCalledWith("npm install");
  });

  it("shows the copied state after a successful copy", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    const user = userEvent.setup();
    setClipboard({ writeText });
    render(<CopyableSnippet code="echo hi" isCopyable />);

    const button = screen.getByRole("button", { name: "Copy code" });
    // Before copying, the copy icon is shown, not the green check.
    expect(button.querySelector(".text-green-500")).not.toBeInTheDocument();

    await user.click(button);

    // Copied -> the check icon (green) replaces the copy icon.
    await waitFor(() =>
      expect(button.querySelector(".text-green-500")).toBeInTheDocument(),
    );
  });

  it("falls back to execCommand when the clipboard API is unavailable", async () => {
    const user = userEvent.setup();
    setClipboard(undefined);
    const execCommand = vi.fn().mockReturnValue(true);
    document.execCommand = execCommand as unknown as typeof document.execCommand;
    render(<CopyableSnippet code="ls -la" isCopyable />);

    await user.click(screen.getByRole("button", { name: "Copy code" }));
    expect(execCommand).toHaveBeenCalledWith("copy");
  });

  it("logs an error when copying fails", async () => {
    const writeText = vi.fn().mockRejectedValue(new Error("denied"));
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    const user = userEvent.setup();
    setClipboard({ writeText });
    render(<CopyableSnippet code="secret" isCopyable />);

    await user.click(screen.getByRole("button", { name: "Copy code" }));
    await waitFor(() =>
      expect(errorSpy).toHaveBeenCalledWith(
        "Failed to copy text:",
        expect.any(Error),
      ),
    );
    errorSpy.mockRestore();
  });

  it("hides the copy button when not copyable", () => {
    render(<CopyableSnippet code="ls -la" isCopyable={false} />);
    expect(
      screen.queryByRole("button", { name: "Copy code" }),
    ).not.toBeInTheDocument();
  });
});
