import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { UserPats } from "./user-pats";

const h = vi.hoisted(() => ({ pats: { isLoading: false, isFetching: false, data: undefined as unknown } }));
vi.mock("@/idp/iam/hooks/use-activity", () => ({
  useGetPats: () => h.pats,
}));
vi.mock("./user-pats-list", () => ({
  UserPATList: ({ data }: { data: unknown[] }) => <div data-testid="pat-list">{data.length}</div>,
}));
vi.mock("./generate-pat-modal", () => ({
  GenerateTokenModal: ({ isOpen }: { isOpen: boolean }) => (
    <div data-testid="generate-modal">{isOpen ? "open" : "closed"}</div>
  ),
}));

describe("UserPats", () => {
  beforeEach(() => {
    h.pats = { isLoading: false, isFetching: false, data: undefined };
  });

  it("shows the empty state and a generate button when there are no tokens", async () => {
    h.pats = { isLoading: false, isFetching: false, data: [] };
    const user = userEvent.setup();
    render(<UserPats id="u1" />);
    expect(screen.getByText(/No PAT.*generated/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Generate PAT" }));
    expect(screen.getByTestId("generate-modal")).toHaveTextContent("open");
  });

  it("renders the token list when tokens exist", () => {
    h.pats = { isLoading: false, isFetching: false, data: [{}, {}, {}] };
    render(<UserPats id="u1" />);
    expect(screen.getByTestId("pat-list")).toHaveTextContent("3");
  });
});
