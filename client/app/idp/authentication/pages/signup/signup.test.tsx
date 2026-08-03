import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

import { Signup } from "./signup";

const h = vi.hoisted(() => ({
  loginOption: { isLoading: false, data: undefined as unknown },
  signUpSetting: { isLoading: false, data: undefined as unknown },
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useGetLoginOptions: () => h.loginOption,
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetSignUpSetting: () => h.signUpSetting,
}));
vi.mock("./signup-form", () => ({
  SignupForm: (props: Record<string, unknown>) => (
    <div data-testid="signup-form">{JSON.stringify(props)}</div>
  ),
}));

describe("Signup", () => {
  beforeEach(() => {
    h.loginOption = { isLoading: false, data: undefined };
    h.signUpSetting = { isLoading: false, data: undefined };
  });

  it("renders a loading spinner while options load", () => {
    h.loginOption = { isLoading: true, data: undefined };
    const { container } = render(<Signup />);
    expect(container.querySelector(".animate-spin")).toBeInTheDocument();
  });

  it("renders nothing when there are no allowed grant types", () => {
    h.loginOption = { isLoading: false, data: { allowedGrantTypes: [] } };
    const { container } = render(<Signup />);
    expect(container.firstChild).toBeNull();
  });

  it("renders the signup form with resolved sign-up flags", () => {
    h.loginOption = { isLoading: false, data: { allowedGrantTypes: ["password"] } };
    h.signUpSetting = {
      isLoading: false,
      data: { isEmailPasswordSignUpEnabled: true, isSSoSignUpEnabled: false },
    };
    render(<Signup />);
    const form = screen.getByTestId("signup-form");
    expect(form).toHaveTextContent('"emailSignUpEnabled":true');
    expect(form).toHaveTextContent('"ssoSignUpEnabled":false');
  });
});
