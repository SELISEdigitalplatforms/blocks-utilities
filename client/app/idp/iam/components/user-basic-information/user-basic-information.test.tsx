import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

import { UserBasicInformation } from "./user-basic-information";

const h = vi.hoisted(() => ({
  me: { isLoading: false, data: undefined as unknown },
  byId: { isLoading: false, data: undefined as unknown },
}));
vi.mock("@/idp/iam/hooks/use-user", () => ({
  useGetMe: () => h.me,
  useGetUserById: () => h.byId,
}));

const user = {
  firstName: "Ada",
  lastName: "Lovelace",
  email: "ada@calc.dev",
  logInCount: 7,
  active: true,
  lastLoggedInTime: "2025-01-02T10:00:00Z",
  userCreationType: 1,
};

describe("UserBasicInformation", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.me = { isLoading: false, data: undefined };
    h.byId = { isLoading: false, data: undefined };
  });

  it("returns null when not loading and there is no data", () => {
    const { container } = render(<UserBasicInformation id="u1" projectKey="p1" />);
    expect(container.firstChild).toBeNull();
  });

  it("renders the by-id user details", () => {
    h.byId = { isLoading: false, data: { data: user } };
    render(<UserBasicInformation id="u1" projectKey="p1" />);
    expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByText("ada@calc.dev")).toBeInTheDocument();
    expect(screen.getByText("7")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("renders the current user's details when own is set", () => {
    h.me = { isLoading: false, data: { data: { ...user, active: false, logInCount: undefined } } };
    render(<UserBasicInformation id="u1" projectKey="p1" own />);
    expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
    // Missing login count falls back to a dash.
    expect(screen.getByText("-")).toBeInTheDocument();
  });

  it("shows loading skeletons while fetching", () => {
    h.byId = { isLoading: true, data: undefined };
    const { container } = render(<UserBasicInformation id="u1" projectKey="p1" />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });
});
