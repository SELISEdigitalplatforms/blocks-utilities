import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { OidcList } from "./oidc-list";

let hookState: {
  isLoading: boolean;
  isFetching: boolean;
  data: unknown;
};
vi.mock("@blocks-idp/authentication/hooks/use-auth-oidc", () => ({
  useGetAuthOidcCredentials: () => hookState,
}));

vi.mock("./oidc-card", () => ({
  OIDCCard: ({ oidc }: { oidc: { clientDisplayName: string } }) => (
    <div data-testid="oidc-card">{oidc.clientDisplayName}</div>
  ),
}));

describe("OidcList", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    hookState = { isLoading: false, isFetching: false, data: undefined };
    useProjectStore.setState({ selectedProject: { tenantId: "tg-1" } });
  });

  it("shows the loading skeleton while fetching", () => {
    hookState.isLoading = true;
    const { container } = render(<OidcList />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(
      0,
    );
  });

  it("shows the empty state when there are no credentials", () => {
    render(<OidcList />);
    expect(
      screen.getByText(/No OIDC configuration found/),
    ).toBeInTheDocument();
  });

  it("renders one card per credential sorted newest first", () => {
    hookState.data = {
      oIDCClientCredentials: [
        { itemId: "a", clientDisplayName: "Older", createdDate: "2024-01-01" },
        { itemId: "b", clientDisplayName: "Newer", createdDate: "2024-06-01" },
      ],
    };
    render(<OidcList />);
    const cards = screen.getAllByTestId("oidc-card");
    expect(cards).toHaveLength(2);
    expect(cards[0]).toHaveTextContent("Newer");
    expect(cards[1]).toHaveTextContent("Older");
  });

  it("wraps a single (non-array) credential into a list", () => {
    hookState.data = {
      oIDCClientCredentials: {
        itemId: "solo",
        clientDisplayName: "Solo",
        createdDate: "2024-01-01",
      },
    };
    render(<OidcList />);
    expect(screen.getAllByTestId("oidc-card")).toHaveLength(1);
  });
});
