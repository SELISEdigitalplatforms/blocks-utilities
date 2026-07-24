import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { vi } from "vitest";
import { NuqsTestingAdapter } from "nuqs/adapters/testing";

import { MfaCheck } from "./mfa-check";

vi.mock("./mfa-check-form", () => ({
  MfaCheckFrom: () => <div data-testid="mfa-form" />,
}));

const renderAt = (search: string) =>
  render(
    <NuqsTestingAdapter searchParams={search}>
      <MfaCheck />
    </NuqsTestingAdapter>,
  );

describe("MfaCheck", () => {
  it("shows the authenticator-app message for MFA type 1", () => {
    renderAt("?mfa_type=1");
    expect(screen.getByText(/authenticator app/i)).toBeInTheDocument();
    expect(screen.getByTestId("mfa-form")).toBeInTheDocument();
  });

  it("shows the email message for other MFA types", () => {
    renderAt("?mfa_type=2");
    expect(screen.getByText(/Check your email/i)).toBeInTheDocument();
  });
});
