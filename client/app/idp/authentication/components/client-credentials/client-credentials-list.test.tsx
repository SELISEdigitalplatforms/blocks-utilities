import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { ClientCredentialList } from "./client-credentials-list";

let hookState: { isLoading: boolean; isFetching: boolean; data: unknown };
vi.mock("@blocks-idp/authentication/hooks/use-auth-clients", () => ({
  useGetAuthClientCredentials: () => hookState,
}));

vi.mock("./client-credential-card", () => ({
  ClientCredentialsCard: ({
    clientCredential,
  }: {
    clientCredential: { name: string };
  }) => <div data-testid="cc-card">{clientCredential.name}</div>,
}));

describe("ClientCredentialList", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    hookState = { isLoading: false, isFetching: false, data: undefined };
    useProjectStore.setState({ selectedProject: { tenantId: "tg-1" } });
  });

  it("shows the loading skeleton while fetching", () => {
    hookState.isFetching = true;
    const { container } = render(<ClientCredentialList />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(
      0,
    );
  });

  it("shows the empty state when there are no client credentials", () => {
    hookState.data = [];
    render(<ClientCredentialList />);
    expect(screen.getByText(/No client credential found/)).toBeInTheDocument();
  });

  it("renders one card per credential sorted newest first", () => {
    hookState.data = [
      { itemId: "a", name: "Older", createdDate: "2024-01-01" },
      { itemId: "b", name: "Newer", createdDate: "2024-06-01" },
    ];
    render(<ClientCredentialList />);
    const cards = screen.getAllByTestId("cc-card");
    expect(cards).toHaveLength(2);
    expect(cards[0]).toHaveTextContent("Newer");
    expect(cards[1]).toHaveTextContent("Older");
  });
});
