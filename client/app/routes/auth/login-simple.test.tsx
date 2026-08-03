import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import LoginSimplePage from "./login-simple";

vi.mock("@/components/blocks-login-page", () => ({
  BlocksLoginPage: ({
    name,
    onLogin,
    isLoading,
  }: {
    name: string;
    onLogin: () => void;
    isLoading: boolean;
  }) => (
    <button onClick={onLogin} disabled={isLoading}>
      login-{name}
    </button>
  ),
}));

vi.mock("@/lib/runtime-env", () => ({
  getRuntimeEnv: (key: string) => (key === "BLOCKS_X_BLOCKS_KEY" ? "bk" : "cid"),
}));

const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

vi.mock("@/constants/endpoint.constant", () => ({
  API_BASES: { IDP: "https://api.test" },
}));

describe("LoginSimplePage", () => {
  let hrefValue: string;
  beforeEach(() => {
    vi.clearAllMocks();
    hrefValue = "";
    Object.defineProperty(window, "location", {
      configurable: true,
      value: {
        origin: "https://app.test",
        get href() {
          return hrefValue;
        },
        set href(v: string) {
          hrefValue = v;
        },
      },
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders the login page with the app name", () => {
    render(<LoginSimplePage />);
    expect(screen.getByText("login-blocks-utilities")).toBeInTheDocument();
  });

  it("redirects to the authorization url returned by the API", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        json: () => Promise.resolve({ redirect_uri: "https://idp.test/authorize" }),
      }),
    );
    const user = userEvent.setup();
    render(<LoginSimplePage />);

    await user.click(screen.getByText("login-blocks-utilities"));
    await waitFor(() => expect(hrefValue).toBe("https://idp.test/authorize"));
  });

  it("shows an error toast when no authorization url is returned", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({ json: () => Promise.resolve({}) }),
    );
    const user = userEvent.setup();
    render(<LoginSimplePage />);

    await user.click(screen.getByText("login-blocks-utilities"));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({
        errors: "Failed to get authorization URL",
      }),
    );
  });

  it("shows an error toast when the request throws", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("network")));
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    const user = userEvent.setup();
    render(<LoginSimplePage />);

    await user.click(screen.getByText("login-blocks-utilities"));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({
        errors: "Unable to start login. Please try again.",
      }),
    );
    errorSpy.mockRestore();
  });
});
