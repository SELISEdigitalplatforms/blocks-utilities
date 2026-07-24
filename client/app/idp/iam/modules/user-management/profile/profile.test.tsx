import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { Profile, UserProfile } from "./profile";

let meState: { isPending: boolean; isLoading: boolean; data: unknown };
vi.mock("@/idp/iam/hooks/use-user", () => ({
  useGetMe: () => meState,
}));

const setTabId = vi.fn();
let tabId = "details";
vi.mock("nuqs", () => ({
  useQueryState: () => [tabId, setTabId],
}));

vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: () => "bk" }));

vi.mock("../update-user/update-user", () => ({
  UpdateUser: () => <div data-testid="update-user" />,
}));
vi.mock("@/idp/iam/components/profile-details/profile-details", () => ({
  ProfileDetails: () => <div data-testid="profile-details" />,
}));
vi.mock("../user-devices/user-devices", () => ({
  UserDevices: () => <div data-testid="user-devices" />,
}));
vi.mock("../user-histories/user-histories", () => ({
  UserHistories: () => <div data-testid="user-histories" />,
}));
vi.mock("../user-pat/user-pats", () => ({
  UserPats: () => <div data-testid="user-pats" />,
}));

describe("Profile", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    tabId = "details";
    meState = {
      isPending: false,
      isLoading: false,
      data: { data: { itemId: "u1", firstName: "Ada", lastName: "Lovelace" } },
    };
  });

  it("renders nothing while the current user loads", () => {
    meState = { isPending: true, isLoading: false, data: undefined };
    const { container } = render(<Profile />);
    expect(container.firstChild).toBeNull();
  });

  it("renders the user profile with tabs when data is present", () => {
    render(<Profile />);
    expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Details" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "PATs" })).toBeInTheDocument();
    expect(screen.getByTestId("update-user")).toBeInTheDocument();
  });

  it("shows the profile details for the details tab", () => {
    render(<UserProfile id="u1" />);
    expect(screen.getByTestId("profile-details")).toBeInTheDocument();
  });
});
