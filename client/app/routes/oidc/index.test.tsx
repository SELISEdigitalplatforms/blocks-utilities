import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import OidcIndexPage from "./index";

const navigate = vi.fn();
let search = "";
vi.mock("react-router", async () => {
  const actual =
    await vi.importActual<typeof import("react-router")>("react-router");
  return {
    ...actual,
    useNavigate: () => navigate,
    useSearchParams: () => [new URLSearchParams(search)],
  };
});

const verifyOidc = vi.fn();
vi.mock("@blocks-idp/authentication/services/auth.service", () => ({
  authService: { verifyOidc: (...a: unknown[]) => verifyOidc(...a) },
}));
vi.mock("@blocks-idp/authentication/pages/oidc/permission-wrapper", () => ({
  OIDCPermissionWrapper: () => <div data-testid="permission-wrapper" />,
}));
vi.mock("@blocks-idp/authentication/pages/oidc/oidc-signin", () => ({
  OIDCSignin: () => <div data-testid="oidc-signin" />,
}));
const setAuthenticated = vi.fn();
const setTokens = vi.fn();
vi.mock("@seliseblocks/genesis-os", () => ({
  useAuthStore: () => ({ setAuthenticated, setTokens }),
}));
vi.mock("@/lib/runtime-env", () => ({
  getRuntimeEnv: () => "https://localhost/api",
}));

const renderPage = () =>
  render(
    <MemoryRouter>
      <OidcIndexPage />
    </MemoryRouter>,
  );

describe("OidcIndexPage", () => {
  const originalLocation = window.location;
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    search = "";
    Object.defineProperty(window, "location", {
      configurable: true,
      value: { ...originalLocation, href: "", origin: "https://app" },
    });
  });
  afterEach(() => {
    Object.defineProperty(window, "location", {
      configurable: true,
      value: originalLocation,
    });
  });

  it("renders the signin screen when there are no params", () => {
    renderPage();
    expect(screen.getByTestId("oidc-signin")).toBeInTheDocument();
  });

  it("renders the permission wrapper when a userName is present", () => {
    search = "userName=jane";
    renderPage();
    expect(screen.getByTestId("permission-wrapper")).toBeInTheDocument();
  });

  it("exchanges the code and redirects on success", async () => {
    search = "code=c1&state=s1";
    verifyOidc.mockResolvedValue({ access_token: "at", refresh_token: "rt" });
    renderPage();
    await waitFor(() => expect(setAuthenticated).toHaveBeenCalled());
    expect(setTokens).toHaveBeenCalledWith("at", "rt");
    expect(localStorage.getItem("oidc-auth-storage")).toContain("at");
    expect(window.location.href).toBe("https://app/email");
  });

  it("navigates to the error page when the exchange fails", async () => {
    search = "code=c1&state=s1";
    verifyOidc.mockRejectedValue(new Error("bad"));
    renderPage();
    await waitFor(() =>
      expect(navigate).toHaveBeenCalledWith("/oidc/error"),
    );
  });
});
