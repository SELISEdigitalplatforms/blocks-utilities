import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { AddEnvironmentModal } from "./add-environment-modal";

const mutateAsync = vi.fn();
let isPending = false;
vi.mock("@blocks-identifier/hooks/use-project", () => ({
  useCreateProject: () => ({ mutateAsync, isPending }),
}));

describe("AddEnvironmentModal", () => {
  beforeEach(() => {
    mutateAsync.mockReset().mockResolvedValue({});
    isPending = false;
  });

  it("lists environment options and disables Add until one is selected", () => {
    render(<AddEnvironmentModal onClose={() => {}} tenantGroupId="tg1" />);
    expect(screen.getByText("Development")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add" })).toBeDisabled();
  });

  it("excludes pre-selected environments", () => {
    render(
      <AddEnvironmentModal
        onClose={() => {}}
        tenantGroupId="tg1"
        preSelectedEnvironments={["dev"]}
      />,
    );
    expect(screen.queryByText("Development")).not.toBeInTheDocument();
    expect(screen.getByText("Testing")).toBeInTheDocument();
  });

  it("creates the project and reports the sorted selection on Add", async () => {
    const onClose = vi.fn();
    render(
      <AddEnvironmentModal
        onClose={onClose}
        tenantGroupId="tg1"
        projectName="Demo"
      />,
    );
    // Select two options out of order; expect index-sorted output.
    fireEvent.click(screen.getByText("Staging").previousSibling as Element);
    fireEvent.click(screen.getByText("Development").previousSibling as Element);
    const add = screen.getByRole("button", { name: "Add" });
    expect(add).toBeEnabled();
    fireEvent.click(add);
    await waitFor(() => expect(mutateAsync).toHaveBeenCalled());
    expect(mutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({ name: "Demo", tenantGroupId: "tg1" }),
    );
    expect(onClose).toHaveBeenCalledWith(["dev", "stg"]);
  });

  it("closes with an empty selection on Cancel", () => {
    const onClose = vi.fn();
    render(<AddEnvironmentModal onClose={onClose} tenantGroupId="tg1" />);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onClose).toHaveBeenCalledWith([]);
    expect(mutateAsync).not.toHaveBeenCalled();
  });
});
