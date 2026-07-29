import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { ViewAuthConfigure } from "./view-auth-configure";

let configState: { data: unknown; isLoading: boolean; isFetching: boolean };
vi.mock("@blocks-idp/authentication/hooks/use-auth-config", () => ({
  useGetAuthConfig: () => configState,
}));

vi.mock("./url-with-actions", () => ({
  UrlWithActions: ({ url }: { url: string }) => (
    <div data-testid="url-with-actions">{url}</div>
  ),
}));

describe("ViewAuthConfigure", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    configState = { data: undefined, isLoading: false, isFetching: false };
    useProjectStore.setState({ selectedProject: { tenantId: "tg-1" } });
  });

  it("shows the loading skeleton while fetching", () => {
    configState.isLoading = true;
    const { container } = render(<ViewAuthConfigure />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(
      0,
    );
  });

  it("renders the configured values when loaded", () => {
    configState.data = {
      accessTokenValidForNumberMinutes: 30,
      refreshTokenValidForNumberMinutes: 60,
      rememberMeRefreshTokenValidForNumberMinutes: 120,
      getNumberOfWrongAttemptsToLockTheAccount: 5,
      accountLockDurationInMinutes: 15,
      publicCertificatePath: "https://cert.test/pub.pem",
    };
    render(<ViewAuthConfigure />);
    expect(screen.getByText("Access Token Validity")).toBeInTheDocument();
    expect(screen.getByText(/30/)).toBeInTheDocument();
    expect(screen.getByTestId("url-with-actions")).toHaveTextContent(
      "https://cert.test/pub.pem",
    );
  });
});
