import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

import { UserHistories } from "./user-histories";

const h = vi.hoisted(() => ({ histories: { isLoading: false, isFetching: false, data: undefined as unknown } }));
vi.mock("@/idp/iam/hooks/use-activity", () => ({
  useGetHistories: () => h.histories,
}));
vi.mock("./user-history-list", () => ({
  UserHistoryList: ({ data }: { data: unknown[] }) => (
    <div data-testid="history-list">{data.length}</div>
  ),
}));

describe("UserHistories", () => {
  beforeEach(() => {
    h.histories = { isLoading: false, isFetching: false, data: undefined };
  });

  it("renders the history list with the fetched rows", () => {
    h.histories = { isLoading: false, isFetching: false, data: { data: [{}], totalCount: 1 } };
    render(<UserHistories id="u1" projectKey="p1" />);
    expect(screen.getByTestId("history-list")).toHaveTextContent("1");
  });

  it("renders pagination when the total exceeds the page size", () => {
    h.histories = {
      isLoading: false,
      isFetching: false,
      data: { data: new Array(10).fill({}), totalCount: 30 },
    };
    render(<UserHistories id="u1" projectKey="p1" />);
    expect(screen.getAllByRole("button").length).toBeGreaterThan(0);
  });
});
