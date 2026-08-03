import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { AddSSOPermission } from "./add-sso-permission";

let permsResult: { data?: { data: unknown[]; totalCount: number }; isLoading: boolean } = {
  data: {
    data: [
      { itemId: "1", resource: "users", name: "Users", type: 1 },
      { itemId: "2", resource: "roles", name: "Roles", type: 1 },
    ],
    totalCount: 2,
  },
  isLoading: false,
};
vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({
  useGetPermissions: () => permsResult,
}));

describe("AddSSOPermission", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedProject: { tenantId: "tg1" } });
    permsResult = {
      data: {
        data: [
          { itemId: "1", resource: "users", name: "Users", type: 1 },
          { itemId: "2", resource: "roles", name: "Roles", type: 1 },
        ],
        totalCount: 2,
      },
      isLoading: false,
    };
  });

  it("disables the trigger once five permissions already exist", () => {
    render(
      <AddSSOPermission
        permissions={Array.from({ length: 5 }, (_, i) => ({ resource: `r${i}` })) as never}
        onAdd={vi.fn()}
      />,
    );
    expect(
      screen.getByRole("button", { name: /Assign Permissions/ }),
    ).toBeDisabled();
  });

  it("opens, selects a permission and calls onAdd with the selection", async () => {
    const user = userEvent.setup();
    const onAdd = vi.fn();
    render(<AddSSOPermission permissions={[]} onAdd={onAdd} />);
    await user.click(screen.getByRole("button", { name: /Assign Permissions/ }));
    expect(await screen.findByText("Users")).toBeInTheDocument();

    const checkboxes = screen.getAllByRole("checkbox");
    await user.click(checkboxes[0]);
    const add = screen.getByRole("button", { name: "Add" });
    await waitFor(() => expect(add).toBeEnabled());
    await user.click(add);
    expect(onAdd).toHaveBeenCalledWith([
      expect.objectContaining({ resource: "users" }),
    ]);
  });

  it("keeps Add disabled when nothing is selected", async () => {
    const user = userEvent.setup();
    render(<AddSSOPermission permissions={[]} onAdd={vi.fn()} />);
    await user.click(screen.getByRole("button", { name: /Assign Permissions/ }));
    await screen.findByText("Users");
    expect(screen.getByRole("button", { name: "Add" })).toBeDisabled();
  });
});
