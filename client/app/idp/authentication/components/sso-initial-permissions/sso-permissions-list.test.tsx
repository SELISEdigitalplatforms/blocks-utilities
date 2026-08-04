import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { SSOPermissionsList } from "./sso-permissions-list";

vi.mock("./delete-sso-permission", () => ({
  DeleteSSOPermission: ({
    permission,
  }: {
    permission: { name: string };
  }) => <button>del-{permission.name}</button>,
}));

const permissions = [
  { itemId: "p1", name: "read:users", resource: "users" },
  { itemId: "p2", name: "write:roles", resource: "roles" },
] as never;

describe("SSOPermissionsList", () => {
  it("renders the header and a row per permission", () => {
    render(<SSOPermissionsList permissions={permissions} onDelete={vi.fn()} />);
    expect(screen.getByText("Name")).toBeInTheDocument();
    expect(screen.getByText("Resource")).toBeInTheDocument();
    expect(screen.getByText("read:users")).toBeInTheDocument();
    expect(screen.getByText("write:roles")).toBeInTheDocument();
    expect(screen.getByText("users")).toBeInTheDocument();
  });

  it("renders a delete action per row", () => {
    render(<SSOPermissionsList permissions={permissions} onDelete={vi.fn()} />);
    expect(screen.getByText("del-read:users")).toBeInTheDocument();
    expect(screen.getByText("del-write:roles")).toBeInTheDocument();
  });

  it("keeps keyboard events raised inside the actions cell from reaching the row", () => {
    const outerKeyDown = vi.fn();
    render(
      <div onKeyDown={outerKeyDown}>
        <SSOPermissionsList permissions={permissions} onDelete={vi.fn()} />
      </div>,
    );

    fireEvent.keyDown(screen.getByRole("button", { name: "del-read:users" }), {
      key: "Enter",
    });
    expect(outerKeyDown).not.toHaveBeenCalled();

    // A key raised outside the actions cell still bubbles as usual.
    fireEvent.keyDown(screen.getByText("read:users"), { key: "Enter" });
    expect(outerKeyDown).toHaveBeenCalledTimes(1);
  });

  it("shows the empty state when there are no permissions", () => {
    render(<SSOPermissionsList permissions={[]} onDelete={vi.fn()} />);
    expect(screen.getByText("No permissions found")).toBeInTheDocument();
  });
});
