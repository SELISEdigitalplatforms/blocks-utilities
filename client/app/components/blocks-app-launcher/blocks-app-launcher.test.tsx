import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { BlocksAppLauncher } from "./blocks-app-launcher";

const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const renderLauncher = () =>
  render(
    <MemoryRouter>
      <BlocksAppLauncher />
    </MemoryRouter>,
  );

describe("BlocksAppLauncher", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("seeds default favourites and lists them in the popover", async () => {
    const user = userEvent.setup();
    renderLauncher();
    await user.click(screen.getByRole("button", { name: "SELISE Blocks apps" }));
    expect(await screen.findByText("Your favourites")).toBeInTheDocument();
    expect(screen.getByText("IAM")).toBeInTheDocument();
    expect(screen.getByText("Localization")).toBeInTheDocument();
    expect(screen.getByText("More from SELISE Blocks")).toBeInTheDocument();
  });

  it("opens the manage favourites dialog and toggles a favourite", async () => {
    const user = userEvent.setup();
    renderLauncher();
    await user.click(screen.getByRole("button", { name: "SELISE Blocks apps" }));
    await user.click(screen.getByRole("button", { name: "Edit favourites" }));
    expect(await screen.findByText("Manage Favourites")).toBeInTheDocument();
    const osButton = screen
      .getAllByRole("button", { pressed: false })
      .find((b) => b.textContent?.includes("OS"))!;
    await user.click(osButton);
    await waitFor(() => {
      const stored = JSON.parse(
        localStorage.getItem("blocks-app-favourites") || "[]",
      );
      expect(stored).toContain("os");
    });
  });

  it("shows an error toast when the initiate call has no redirect", async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({ json: () => Promise.resolve({}) }),
    );
    renderLauncher();
    await user.click(screen.getByRole("button", { name: "SELISE Blocks apps" }));
    await user.click(await screen.findByText("IAM"));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({
        errors: "Failed to get authorization URL",
      }),
    );
  });

  it("shows an error toast when the initiate call throws", async () => {
    const user = userEvent.setup();
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("network")));
    renderLauncher();
    await user.click(screen.getByRole("button", { name: "SELISE Blocks apps" }));
    await user.click(await screen.findByText("IAM"));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({
        errors: "Unable to open app. Please try again.",
      }),
    );
  });
});
