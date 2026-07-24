import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { CreateProjectEnvironmentsForm } from "./create-project-environments-form";
import { useCreateProjectFormState } from "../../utils";

const saveProject = vi.fn();
let isPending = false;
vi.mock("@blocks-identifier/hooks/use-project", () => ({
  useProjectForm: () => ({ isPending, saveProject }),
}));

describe("CreateProjectEnvironmentsForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
    useCreateProjectFormState.getState().resetFormData();
  });

  it("renders the environment options", () => {
    render(<CreateProjectEnvironmentsForm />);
    expect(screen.getByText("Select environments")).toBeInTheDocument();
    expect(screen.getByText("Development")).toBeInTheDocument();
    expect(screen.getByText("Testing")).toBeInTheDocument();
  });

  it("keeps submit disabled until an environment is selected", () => {
    render(<CreateProjectEnvironmentsForm />);
    expect(screen.getByRole("button", { name: "Submit" })).toBeDisabled();
  });

  it("saves the project when a selection is submitted", async () => {
    render(<CreateProjectEnvironmentsForm />);
    fireEvent.click(screen.getAllByRole("checkbox")[0]);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Submit" })).toBeEnabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: "Submit" }));
    await waitFor(() => expect(saveProject).toHaveBeenCalled());
    expect(useCreateProjectFormState.getState().formData[2].environments).toHaveLength(1);
  });
});
