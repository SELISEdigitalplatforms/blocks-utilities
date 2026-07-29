import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { OidcSigninForm, signinByEmail } from "./oidc-signin-form";

const navigate = vi.fn();
vi.mock("react-router", async () => {
  const actual =
    await vi.importActual<typeof import("react-router")>("react-router");
  return { ...actual, useNavigate: () => navigate };
});

let context = {
  themeColor: "#123456",
  projectKey: "pk1",
  clientId: "c1",
  scope: "openid",
  state: "st",
  redirectUri: "https://app/cb",
  nonce: "n1",
};
vi.mock("@/layouts/oidc-layout", () => ({
  useOIDCContext: () => context,
}));

vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  buildOIDCNavigationUrl: (p: string) => `/base${p}`,
  getCurrentOIDCParams: () => new URLSearchParams("a=1"),
}));

const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const setAuthenticated = vi.fn();
vi.mock("@seliseblocks/genesis-os", () => ({
  useAuthStore: () => ({ setAuthenticated }),
}));

const fill = () => {
  fireEvent.change(screen.getByPlaceholderText("Enter your email"), {
    target: { value: "user@example.com" },
  });
  fireEvent.change(screen.getByPlaceholderText("Enter your password"), {
    target: { value: "secret" },
  });
};

const renderForm = () =>
  render(
    <MemoryRouter>
      <OidcSigninForm />
    </MemoryRouter>,
  );

describe("OidcSigninForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    context = {
      themeColor: "#123456",
      projectKey: "pk1",
      clientId: "c1",
      scope: "openid",
      state: "st",
      redirectUri: "https://app/cb",
      nonce: "n1",
    };
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it("renders email and password fields and the forgot-password link", () => {
    renderForm();
    expect(screen.getByPlaceholderText("Enter your email")).toBeInTheDocument();
    expect(
      screen.getByPlaceholderText("Enter your password"),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Forgot password?" }),
    ).toHaveAttribute("href", "/base/oidc/forgot-password");
  });

  it("navigates to the permission page after a successful login", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(JSON.stringify({ access_token: "tok" })),
      }),
    );
    renderForm();
    fill();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() => expect(setAuthenticated).toHaveBeenCalled());
    expect(localStorage.getItem("oidc-auth-storage")).toContain("tok");
    expect(navigate).toHaveBeenCalledWith(
      expect.stringContaining("/oidc/permission?"),
    );
  });

  it("routes to the mfa check page when the response requires mfa", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        text: () =>
          Promise.resolve(
            JSON.stringify({ enable_mfa: true, mfaId: "m1", mfaType: "totp" }),
          ),
      }),
    );
    renderForm();
    fill();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() =>
      expect(navigate).toHaveBeenCalledWith(
        "/base/mfa-check?mfa_id=m1&mfa_type=totp",
      ),
    );
    expect(setAuthenticated).not.toHaveBeenCalled();
  });

  it("shows an error toast when the project key is missing", async () => {
    context = { ...context, projectKey: "" };
    renderForm();
    fill();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({
        errors: "Project key is required",
      }),
    );
  });

  it("navigates to the error screen with the api error details", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: false,
        status: 401,
        statusText: "Unauthorized",
        text: () =>
          Promise.resolve(
            JSON.stringify({
              error: "invalid_grant",
              error_description: "bad creds",
            }),
          ),
      }),
    );
    renderForm();
    fill();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() =>
      expect(navigate).toHaveBeenCalledWith(
        expect.stringContaining("/base/oidc/error"),
      ),
    );
    const target = navigate.mock.calls.at(-1)?.[0] as string;
    expect(target).toContain("error=invalid_grant");
    expect(target).toContain("bad+creds");
  });
});

describe("signinByEmail", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("returns a synthetic token when the response body is empty", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({ ok: true, text: () => Promise.resolve("") }),
    );
    const res = await signinByEmail({
      username: "u",
      password: "p",
      projectKey: "pk",
    });
    expect(res.access_token).toBe("authenticated");
  });

  it("throws the parsed error json on a non-ok response", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: false,
        status: 400,
        statusText: "Bad",
        text: () => Promise.resolve(JSON.stringify({ error: "nope" })),
      }),
    );
    await expect(
      signinByEmail({ username: "u", password: "p", projectKey: "pk" }),
    ).rejects.toEqual({ error: "nope" });
  });

  it("throws an http error when the failure body is not json", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: false,
        status: 500,
        statusText: "Server Error",
        text: () => Promise.resolve("boom"),
      }),
    );
    await expect(
      signinByEmail({ username: "u", password: "p", projectKey: "pk" }),
    ).rejects.toThrow("HTTP 500");
  });
});
