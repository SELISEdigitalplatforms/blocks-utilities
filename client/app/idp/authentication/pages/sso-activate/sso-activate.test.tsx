import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { SsoActivate } from "./sso-activate";

const navigate = vi.fn();
vi.mock("react-router", async () => {
  const actual =
    await vi.importActual<typeof import("react-router")>("react-router");
  return { ...actual, useNavigate: () => navigate };
});

const setAuthenticated = vi.fn();
const setTokens = vi.fn();
vi.mock("@seliseblocks/genesis-os", () => ({
  useAuthStore: () => ({ setAuthenticated, setTokens }),
}));

const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

vi.mock("@/lib/runtime-env", () => ({
  getRuntimeEnv: (k: string) =>
    k === "BLOCKS_LOCALIZATION_BASE_URL" ? "https://api.example" : "key-123",
}));

const getSocialLoginEndpoint = vi.fn();
vi.mock("@blocks-idp/authentication/services/oauth.service", () => ({
  oauthService: {
    getSocialLoginEndpoint: (...a: unknown[]) => getSocialLoginEndpoint(...a),
  },
}));

const renderComp = (code = "the-code", username = "jane@example.com") =>
  render(
    <MemoryRouter>
      <SsoActivate oauthParams={{ code, username }} />
    </MemoryRouter>,
  );

describe("SsoActivate", () => {
  const originalLocation = window.location;

  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    sessionStorage.setItem("clicked_sso_provider", "github");
    sessionStorage.setItem("clicked_sso_audience", "aud1");
    Object.defineProperty(window, "location", {
      configurable: true,
      value: { ...originalLocation, href: "" },
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    Object.defineProperty(window, "location", {
      configurable: true,
      value: originalLocation,
    });
  });

  it("renders the provider account and disables continue until terms accepted", () => {
    renderComp();
    expect(screen.getByText("jane@example.com")).toBeInTheDocument();
    expect(screen.getByText(/Signing in with/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Continue" })).toBeDisabled();
  });

  it("enables continue once terms are accepted", () => {
    renderComp();
    fireEvent.click(screen.getByRole("checkbox"));
    expect(screen.getByRole("button", { name: "Continue" })).toBeEnabled();
  });

  it("activates the account and navigates on success", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: () =>
          Promise.resolve({
            access_token: "at",
            refresh_token: "rt",
          }),
      }),
    );
    renderComp();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));
    await waitFor(() => expect(setAuthenticated).toHaveBeenCalled());
    expect(navigate).toHaveBeenCalledWith("/services/language");
    expect(sessionStorage.getItem("clicked_sso_provider")).toBeNull();
  });

  it("shows an error toast when activation fails", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: false,
        json: () => Promise.resolve({ errors: "bad" }),
      }),
    );
    renderComp();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "bad" }),
    );
    expect(setAuthenticated).not.toHaveBeenCalled();
  });

  it("redirects when using a different account", async () => {
    getSocialLoginEndpoint.mockResolvedValue({
      providerUrl: "https://provider/login",
    });
    renderComp();
    fireEvent.click(screen.getByRole("button", { name: /Use a different/ }));
    await waitFor(() => expect(getSocialLoginEndpoint).toHaveBeenCalled());
    await waitFor(() =>
      expect(window.location.href).toContain("https://provider/login"),
    );
  });

  it("switches account from the keyboard alone", async () => {
    const user = userEvent.setup();
    getSocialLoginEndpoint.mockResolvedValue({
      providerUrl: "https://provider/login",
    });
    renderComp();
    const control = screen.getByRole("button", { name: /Use a different/ });
    control.focus();
    expect(control).toHaveFocus();
    await user.keyboard("{Enter}");
    await waitFor(() => expect(getSocialLoginEndpoint).toHaveBeenCalled());
  });

  it("surfaces the endpoint error when switching account fails", async () => {
    getSocialLoginEndpoint.mockResolvedValue({ error: "denied" });
    renderComp();
    fireEvent.click(screen.getByRole("button", { name: /Use a different/ }));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "denied" }),
    );
  });
});
