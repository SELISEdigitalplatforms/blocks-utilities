import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { UserPATList } from "./user-pats-list";

vi.mock("./generate-pat-modal", () => ({
  GenerateTokenModal: ({ isOpen }: { isOpen: boolean }) =>
    isOpen ? <div data-testid="generate-modal" /> : null,
}));
vi.mock("@/hooks/use-is-mobile", () => ({ default: () => false }));

const future = new Date(Date.now() + 86400000).toISOString();
const past = new Date(Date.now() - 86400000).toISOString();

const rows = [
  { note: "CI token", code: "tok-active", expiryDate: future },
  { note: "", code: "tok-expired", expiryDate: past },
];

describe("UserPATList", () => {
  beforeEach(() => vi.clearAllMocks());

  it("renders the loading skeleton", () => {
    const { container } = render(
      <UserPATList isLoading data={[]} id="u1" />,
    );
    expect(container.querySelectorAll(".rounded-xl").length).toBeGreaterThan(0);
  });

  it("renders active and expired PAT rows", () => {
    render(<UserPATList isLoading={false} data={rows} id="u1" />);
    expect(screen.getByText("CI token")).toBeInTheDocument();
    expect(screen.getByText("active")).toBeInTheDocument();
    expect(screen.getByText("expired")).toBeInTheDocument();
  });

  it("shows the empty state", () => {
    render(<UserPATList isLoading={false} data={[]} id="u1" />);
    expect(screen.getByText("No results.")).toBeInTheDocument();
  });

  it("opens the generate token modal", () => {
    render(<UserPATList isLoading={false} data={[]} id="u1" />);
    fireEvent.click(screen.getByRole("button", { name: "Generate PAT" }));
    expect(screen.getByTestId("generate-modal")).toBeInTheDocument();
  });
});
