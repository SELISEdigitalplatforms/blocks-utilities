import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";

import { UserDropdownMenu } from "./user-dropdown-menu";

const h = vi.hoisted(() => ({ me: { data: undefined as unknown } }));
vi.mock("@/idp/iam/hooks/use-user", () => ({
  useGetMe: () => h.me,
}));
vi.mock("@/components/auth/log-out-button", () => ({
  LogOutButton: () => <span>Log out</span>,
}));

const renderMenu = () =>
  render(
    <MemoryRouter>
      <UserDropdownMenu />
    </MemoryRouter>,
  );

describe("UserDropdownMenu", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.me = { data: undefined };
  });

  it("renders the fallback user icon when there is no user data", () => {
    const { container } = renderMenu();
    // The trigger button renders and no avatar image is present.
    expect(container.querySelector("button")).toBeInTheDocument();
    expect(container.querySelector("img")).not.toBeInTheDocument();
  });

  it("renders the user's initials when a name is present", () => {
    h.me = { data: { data: { firstName: "Ada", lastName: "Lovelace" } } };
    renderMenu();
    expect(screen.getByText("AL")).toBeInTheDocument();
  });

  it("renders the profile image when a profile image url is present", () => {
    h.me = { data: { data: { profileImageUrl: "https://img.test/a.png" } } };
    const { container } = renderMenu();
    const img = container.querySelector("img");
    expect(img).toBeInTheDocument();
    expect(img).toHaveAttribute("src", "https://img.test/a.png");
  });
});
