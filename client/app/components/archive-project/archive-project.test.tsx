import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { ArchivedProject } from "./archive-project";

const navigate = vi.fn();
vi.mock("react-router", async () => {
  const actual =
    await vi.importActual<typeof import("react-router")>(
      "react-router",
    );
  return { ...actual, useNavigate: () => navigate };
});

const disableProject = vi.fn();
let isPending = false;
vi.mock("@blocks-identifier/hooks/use-project", () => ({
  useDisableProject: () => ({ mutateAsync: disableProject, isPending }),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const renderCard = () =>
  render(
    <MemoryRouter>
      <ArchivedProject />
    </MemoryRouter>,
  );

const openAndConfirm = async () => {
  const user = userEvent.setup();
  await user.click(screen.getByRole("button", { name: "Delete" }));
  const buttons = await screen.findAllByRole("button", { name: "Delete" });
  // First is the trigger, the second is the confirm inside the dialog.
  await user.click(buttons[buttons.length - 1]);
};

describe("ArchivedProject", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
    useProjectStore.setState({ selectedProject: { tenantId: "tg-1" } });
  });

  it("opens the confirmation dialog from the delete trigger", async () => {
    const user = userEvent.setup();
    renderCard();
    await user.click(screen.getByRole("button", { name: "Delete" }));
    expect(
      await screen.findByText("Delete this environment?"),
    ).toBeInTheDocument();
  });

  it("disables the project and navigates on success", async () => {
    disableProject.mockResolvedValue({ isSuccess: true });
    renderCard();
    await openAndConfirm();

    await waitFor(() =>
      expect(disableProject).toHaveBeenCalledWith({ projectKey: "tg-1" }),
    );
    expect(showSuccessToast).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith("/console");
  });

  it("shows an error toast when the disable is unsuccessful", async () => {
    disableProject.mockResolvedValue({ isSuccess: false, errors: "boom" });
    renderCard();
    await openAndConfirm();

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "boom" }),
    );
    expect(navigate).not.toHaveBeenCalled();
  });
});
