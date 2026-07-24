import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { LogOutButton } from "./log-out-button";

const logout = vi.fn();
let isPending = false;
vi.mock("@/idp/authentication/hooks/use-auth", () => ({
  useLogout: () => ({ mutateAsync: logout, isPending }),
}));

const queryClientClear = vi.fn();
vi.mock("@/providers/query-provider", () => ({
  getQueryClient: () => ({ clear: queryClientClear }),
}));

const resetSelectedLanguages = vi.fn();
vi.mock("@/cross-modules/localization/store/use-language-view-store", () => ({
  useLanguageViewStore: () => ({ resetSelectedLanguages }),
}));

const resetProjectStore = vi.fn();
const setUnAuthenticated = vi.fn();
const clearTokens = vi.fn();
vi.mock("@seliseblocks/blocks-kit", () => ({
  useProjectStore: () => ({ resetProjectStore }),
  useAuthStore: () => ({ setUnAuthenticated, clearTokens }),
}));

describe("LogOutButton", () => {
  let replace: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
    replace = vi.fn();
    Object.defineProperty(window, "location", {
      configurable: true,
      value: { origin: "https://app.test", replace },
    });
  });

  it("clears session state and redirects to login on click", async () => {
    logout.mockResolvedValue(undefined);
    const user = userEvent.setup();
    render(<LogOutButton />);

    await user.click(screen.getByRole("button", { name: "Logout" }));

    await waitFor(() => expect(logout).toHaveBeenCalled());
    expect(resetProjectStore).toHaveBeenCalled();
    expect(setUnAuthenticated).toHaveBeenCalled();
    expect(clearTokens).toHaveBeenCalled();
    expect(resetSelectedLanguages).toHaveBeenCalled();
    expect(queryClientClear).toHaveBeenCalled();
    expect(replace).toHaveBeenCalledWith("https://app.test/login");
  });

  it("logs an error and does not redirect when logout fails", async () => {
    logout.mockRejectedValue(new Error("network"));
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    const user = userEvent.setup();
    render(<LogOutButton />);

    await user.click(screen.getByRole("button", { name: "Logout" }));

    await waitFor(() => expect(errorSpy).toHaveBeenCalled());
    expect(replace).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });

  it("disables the button while the logout is pending", () => {
    isPending = true;
    render(<LogOutButton />);
    expect(screen.getByRole("button", { name: "Logout" })).toBeDisabled();
  });
});
