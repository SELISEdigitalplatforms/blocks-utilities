import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { AddSSORole } from "./add-sso-role";

let rolesResult: { data?: { data: unknown[]; totalCount: number }; isLoading: boolean } = {
  data: {
    data: [
      { itemId: "1", slug: "admin", name: "Admin" },
      { itemId: "2", slug: "viewer", name: "Viewer" },
    ],
    totalCount: 2,
  },
  isLoading: false,
};
vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useGetRoles: () => rolesResult,
}));

const setRoles = (data: unknown[], loading = false, totalCount = data.length) => {
  rolesResult = { data: { data, totalCount }, isLoading: loading };
};

describe("AddSSORole", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedProject: { tenantId: "tg1" } });
    setRoles([
      { itemId: "1", slug: "admin", name: "Admin" },
      { itemId: "2", slug: "viewer", name: "Viewer" },
    ]);
  });

  it("opens, selects a role and calls onAdd", async () => {
    const user = userEvent.setup();
    const onAdd = vi.fn();
    render(<AddSSORole roles={[]} onAdd={onAdd} />);
    await user.click(screen.getByRole("button", { name: /Assign Role/ }));
    expect(await screen.findByText("Admin")).toBeInTheDocument();
    await user.click(screen.getAllByRole("checkbox")[0]);
    await user.click(screen.getByRole("button", { name: "Add" }));
    expect(onAdd).toHaveBeenCalledWith([
      expect.objectContaining({ slug: "admin" }),
    ]);
  });

  it("shows a loading skeleton", async () => {
    const user = userEvent.setup();
    setRoles([], true);
    render(<AddSSORole roles={[]} onAdd={vi.fn()} />);
    await user.click(screen.getByRole("button", { name: /Assign Role/ }));
    await waitFor(() =>
      expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0),
    );
  });

  it("shows an empty state when there are no roles", async () => {
    const user = userEvent.setup();
    setRoles([]);
    render(<AddSSORole roles={[]} onAdd={vi.fn()} />);
    await user.click(screen.getByRole("button", { name: /Assign Role/ }));
    expect(await screen.findByText("No roles are found")).toBeInTheDocument();
  });
});
