import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

import { UserDevices } from "./user-devices";

const h = vi.hoisted(() => ({ sessions: { isLoading: false, isFetching: false, data: undefined as unknown } }));
vi.mock("@/idp/iam/hooks/use-activity", () => ({
  useGetSessions: () => h.sessions,
}));
vi.mock("./user-devices-list", () => ({
  UserDevicesList: ({ data }: { data: unknown[] }) => (
    <div data-testid="devices-list">{data.length}</div>
  ),
}));

describe("UserDevices", () => {
  beforeEach(() => {
    h.sessions = { isLoading: false, isFetching: false, data: undefined };
  });

  it("renders the devices list with the fetched rows", () => {
    h.sessions = { isLoading: false, isFetching: false, data: { data: [{}, {}], totalCount: 2 } };
    render(<UserDevices id="u1" projectKey="p1" />);
    expect(screen.getByTestId("devices-list")).toHaveTextContent("2");
  });

  it("renders pagination when the total exceeds the page size", () => {
    h.sessions = {
      isLoading: false,
      isFetching: false,
      data: { data: new Array(10).fill({}), totalCount: 25 },
    };
    render(<UserDevices id="u1" projectKey="p1" />);
    expect(screen.getByTestId("devices-list")).toHaveTextContent("10");
    // Pagination renders navigation buttons.
    expect(screen.getAllByRole("button").length).toBeGreaterThan(0);
  });
});
