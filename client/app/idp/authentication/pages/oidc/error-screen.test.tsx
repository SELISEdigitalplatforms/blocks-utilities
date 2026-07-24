import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { OIDCErrorScreen } from "./error-screen";

let params: URLSearchParams;
vi.mock("react-router-dom", async () => {
  const actual =
    await vi.importActual<typeof import("react-router-dom")>(
      "react-router-dom",
    );
  return { ...actual, useSearchParams: () => [params] };
});

vi.mock("@/layouts/oidc-layout", () => ({
  useOIDCContext: () => ({ themeColor: "#124091" }),
}));

const renderScreen = () =>
  render(
    <MemoryRouter>
      <OIDCErrorScreen />
    </MemoryRouter>,
  );

describe("OIDCErrorScreen", () => {
  beforeEach(() => {
    params = new URLSearchParams();
  });

  it("shows the access-blocked state when there is no api error", () => {
    renderScreen();
    expect(screen.getByText("Access Blocked")).toBeInTheDocument();
    expect(
      screen.getByText(/couldn.t sign you in at this time/),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Back to Sign In" }),
    ).toBeInTheDocument();
  });

  it("shows the failure details when an api error is present", () => {
    params = new URLSearchParams({
      error: "invalid_grant",
      error_description: "The token has expired",
    });
    renderScreen();
    expect(screen.getByText("Sign In Failed")).toBeInTheDocument();
    expect(screen.getByText("The token has expired")).toBeInTheDocument();
    // error code is formatted to Title Case with spaces.
    expect(screen.getByText("Invalid Grant")).toBeInTheDocument();
  });
});
