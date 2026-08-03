import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { useProjectStore } from "@seliseblocks/genesis-os";

let projectsResult: { data?: unknown; isLoading: boolean } = {
  data: [{ projects: [{ itemId: "p1", name: "Demo", createdDate: "2024-01-01" }] }],
  isLoading: false,
};
const updateTenantGroup = vi.fn();
let isUpdating = false;

vi.mock("@blocks-identifier/hooks/use-project", () => ({
  useGetProjects: () => projectsResult,
  useUpdateTenantGroup: () => ({ mutateAsync: updateTenantGroup, isPending: isUpdating }),
}));

const toast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  toast: (...a: unknown[]) => toast(...a),
}));

import { SettingsPage } from "./settings";

describe("SettingsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isUpdating = false;
    projectsResult = {
      data: [{ projects: [{ itemId: "p1", name: "Demo", createdDate: "2024-01-01" }] }],
      isLoading: false,
    };
    useProjectStore.setState({
      selectedProject: { itemId: "p1", name: "Demo" },
      selectedTenantGroup: "tg1",
    });
  });

  it("shows the loading skeleton while fetching", () => {
    projectsResult = { data: undefined, isLoading: true };
    const { container } = render(<SettingsPage />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("renders the project general information", () => {
    render(<SettingsPage />);
    expect(screen.getByText("Project Settings")).toBeInTheDocument();
    expect(screen.getByText("Demo")).toBeInTheDocument();
    expect(screen.getByText("Free")).toBeInTheDocument();
  });

  it("opens the edit dialog and saves a new name successfully", async () => {
    updateTenantGroup.mockResolvedValue({});
    render(<SettingsPage />);
    fireEvent.click(screen.getByRole("button", { name: "Edit project name" }));
    const input = await screen.findByLabelText("Project name");
    fireEvent.change(input, { target: { value: "Renamed" } });
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    fireEvent.click(save);
    await waitFor(() =>
      expect(updateTenantGroup).toHaveBeenCalledWith({
        name: "Renamed",
        tenantGroupId: "tg1",
      }),
    );
    expect(toast).toHaveBeenCalledWith(
      expect.objectContaining({ variant: "success" }),
    );
  });

  it("shows an error toast when the update returns errors", async () => {
    updateTenantGroup.mockResolvedValue({ errors: ["bad"] });
    render(<SettingsPage />);
    fireEvent.click(screen.getByRole("button", { name: "Edit project name" }));
    const input = await screen.findByLabelText("Project name");
    fireEvent.change(input, { target: { value: "Renamed" } });
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    fireEvent.click(save);
    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "destructive" }),
      ),
    );
  });
});
