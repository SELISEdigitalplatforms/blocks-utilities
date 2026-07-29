import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { OIDCPermissionScreen } from "./permission";

let context = {
  userName: "Ada",
  themeColor: "#111",
  state: "st",
  nonce: "n",
  scope: "openid",
  redirectUri: "https://app/cb",
  clientId: "c1",
  projectKey: "pk1",
};
vi.mock("@/layouts/oidc-layout", () => ({
  useOIDCContext: () => context,
}));

const userAcknowledgement = vi.fn();
vi.mock("@blocks-idp/authentication/services/oidc-auth-flow.service", () => ({
  userAcknowledgement: (...a: unknown[]) => userAcknowledgement(...a),
}));

const renderScreen = () =>
  render(
    <MemoryRouter>
      <OIDCPermissionScreen />
    </MemoryRouter>,
  );

describe("OIDCPermissionScreen", () => {
  const originalLocation = window.location;
  beforeEach(() => {
    vi.clearAllMocks();
    context = {
      userName: "Ada",
      themeColor: "#111",
      state: "st",
      nonce: "n",
      scope: "openid",
      redirectUri: "https://app/cb",
      clientId: "c1",
      projectKey: "pk1",
    };
    Object.defineProperty(window, "location", {
      configurable: true,
      value: { ...originalLocation, href: "" },
    });
  });
  afterEach(() => {
    Object.defineProperty(window, "location", {
      configurable: true,
      value: originalLocation,
    });
  });

  it("renders the greeting with the user name and consent actions", () => {
    renderScreen();
    expect(screen.getByText("Ada")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Allow" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Deny" })).toBeInTheDocument();
  });

  it("redirects with access_denied when denied", () => {
    renderScreen();
    fireEvent.click(screen.getByRole("button", { name: "Deny" }));
    expect(window.location.href).toContain("error=access_denied");
    expect(window.location.href).toContain("state=st");
  });

  it("redirects to the acknowledgement url when allowed", async () => {
    userAcknowledgement.mockResolvedValue({ redirectUrl: "https://app/done" });
    renderScreen();
    fireEvent.click(screen.getByRole("button", { name: "Allow" }));
    await waitFor(() =>
      expect(window.location.href).toBe("https://app/done"),
    );
    expect(userAcknowledgement).toHaveBeenCalledWith(
      expect.objectContaining({ clientId: "c1", projectKey: "pk1", isAcknowledged: true }),
    );
  });

  it("does not call the service when clientId is missing", async () => {
    context = { ...context, clientId: "" };
    renderScreen();
    fireEvent.click(screen.getByRole("button", { name: "Allow" }));
    await waitFor(() => expect(userAcknowledgement).not.toHaveBeenCalled());
  });
});
