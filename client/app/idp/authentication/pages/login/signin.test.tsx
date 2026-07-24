import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { Signin } from "./signin";

let loginState: { data: unknown; isLoading: boolean };
let signUpState: { data: unknown; isLoading: boolean };
vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useGetLoginOptions: () => loginState,
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetSignUpSetting: () => signUpState,
}));

vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: () => "bk" }));

const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

vi.mock("./signin-form", () => ({
  SigninForm: () => <div data-testid="signin-form" />,
}));
vi.mock("./sso-signin", () => ({
  SsoSignin: () => <div data-testid="sso-signin" />,
}));

const renderSignin = (props = {}) =>
  render(
    <MemoryRouter>
      <Signin {...props} />
    </MemoryRouter>,
  );

describe("Signin", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    loginState = {
      data: { allowedGrantTypes: ["password", "social"] },
      isLoading: false,
    };
    signUpState = {
      data: { isEmailPasswordSignUpEnabled: true },
      isLoading: false,
    };
  });

  it("shows the skeleton while options load", () => {
    loginState = { data: undefined, isLoading: true };
    const { container } = renderSignin();
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(
      0,
    );
  });

  it("renders nothing when there are no allowed grant types", () => {
    loginState = { data: { allowedGrantTypes: [] }, isLoading: false };
    const { container } = renderSignin();
    expect(container.firstChild).toBeNull();
  });

  it("renders the password form, SSO options and sign-up link", () => {
    renderSignin();
    expect(screen.getByText("Blocks Cloud")).toBeInTheDocument();
    expect(screen.getByTestId("signin-form")).toBeInTheDocument();
    expect(screen.getByTestId("sso-signin")).toBeInTheDocument();
    expect(screen.getByText("OR")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Sign up" })).toBeInTheDocument();
  });

  it("omits the sign-up link when sign-up is disabled", () => {
    signUpState = {
      data: { isEmailPasswordSignUpEnabled: false, isSSoSignUpEnabled: false },
      isLoading: false,
    };
    renderSignin();
    expect(
      screen.queryByRole("link", { name: "Sign up" }),
    ).not.toBeInTheDocument();
  });

  it("shows an error toast when an sso error is passed", () => {
    renderSignin({ ssoError: "sso failed" });
    expect(showErrorToast).toHaveBeenCalledWith({ errors: "sso failed" });
  });
});
