import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { EditDomainForm } from "./edit-domain-form";

const mutateAsync = vi.fn();
let isPending = false;
vi.mock("@blocks-identifier/hooks/use-project", () => ({
  useUpdateRepositories: () => ({ mutateAsync, isPending }),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const repos = [
  {
    itemId: "r1",
    repoName: "web",
    repoUrl: "https://github.com/org/web",
    customDeploymentUrl: "https://web.example.com",
  },
];

const renderForm = (
  repositories: typeof repos = repos,
  onAfterSubmit = vi.fn(),
) =>
  render(
    <Dialog open>
      <EditDomainForm
        customDomain="https://example.com"
        repositories={repositories as never}
        onAfterSubmit={onAfterSubmit}
      />
    </Dialog>,
  );

describe("EditDomainForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
    useProjectStore.setState({
      selectedProject: { tenantId: "tg1", environment: "dev" },
    });
  });

  it("renders nothing when there are no repositories", () => {
    const { container } = renderForm([]);
    expect(container.textContent?.trim()).toBe("");
  });

  it("renders a field per repository with the main domain suffix", () => {
    renderForm();
    expect(screen.getByText("web")).toBeInTheDocument();
    expect(screen.getByText(".example.com")).toBeInTheDocument();
  });

  it("submits mapped repo domains and reports success", async () => {
    mutateAsync.mockResolvedValue({ isSuccess: true });
    const onAfterSubmit = vi.fn();
    renderForm(repos, onAfterSubmit);
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(mutateAsync).toHaveBeenCalled());
    const payload = mutateAsync.mock.calls[0][0];
    expect(payload.projectKey).toBe("tg1");
    expect(payload.repoWithDomains[0].customDeploymentDomain).toContain(
      ".example.com",
    );
    expect(showSuccessToast).toHaveBeenCalled();
    expect(onAfterSubmit).toHaveBeenCalled();
  });

  it("shows an error toast when the update is not successful", async () => {
    mutateAsync.mockResolvedValue({ isSuccess: false, errors: ["nope"] });
    renderForm();
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(showErrorToast).toHaveBeenCalledWith({ errors: ["nope"] }));
  });
});
