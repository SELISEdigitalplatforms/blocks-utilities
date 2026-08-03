import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";

import { ResetPassword } from "./reset-password";

vi.mock("./reset-password-form", () => ({
  ResetPasswordForm: ({ code }: { code: string }) => <div data-testid="reset-form">{code}</div>,
}));

describe("ResetPassword", () => {
  it("shows the invalid-link state when no code is present", () => {
    render(<ResetPassword />);
    expect(screen.getByText("Invalid reset link")).toBeInTheDocument();
    expect(screen.queryByTestId("reset-form")).not.toBeInTheDocument();
  });

  it("renders the reset form when a code is present", () => {
    render(<ResetPassword code="abc123" />);
    expect(screen.getByText("Set a new password")).toBeInTheDocument();
    expect(screen.getByTestId("reset-form")).toHaveTextContent("abc123");
  });
});
