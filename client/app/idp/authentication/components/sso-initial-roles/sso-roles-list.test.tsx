import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { SSORolesList } from "./sso-roles-list";

const navigate = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual =
    await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return { ...actual, useNavigate: () => navigate };
});

const onDeleteFromChild = vi.fn();
vi.mock("./delete-sso-role", () => ({
  DeleteSSORole: ({ role }: { role: { name: string } }) => (
    <button onClick={() => onDeleteFromChild(role)}>del-{role.name}</button>
  ),
}));

const roles = [
  { itemId: "r1", name: "Admin", slug: "admin" },
  { itemId: "r2", name: "Viewer", slug: "viewer" },
];

describe("SSORolesList", () => {
  beforeEach(() => vi.clearAllMocks());

  const renderList = (data = roles) =>
    render(
      <MemoryRouter>
        <SSORolesList roles={data as never} onDelete={vi.fn()} />
      </MemoryRouter>,
    );

  it("renders a row per role with its slug badge", () => {
    renderList();
    expect(screen.getByText("Admin")).toBeInTheDocument();
    expect(screen.getByText("viewer")).toBeInTheDocument();
  });

  it("shows the empty state when there are no roles", () => {
    renderList([]);
    expect(screen.getByText("No roles found")).toBeInTheDocument();
  });

  it("navigates to the role detail when a row is clicked", () => {
    renderList();
    fireEvent.click(screen.getByText("Admin"));
    expect(navigate).toHaveBeenCalledWith("/services/iam/role-detail/r1");
  });

  it("does not navigate when the delete action is clicked", () => {
    renderList();
    fireEvent.click(screen.getByText("del-Admin"));
    expect(onDeleteFromChild).toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });
});
