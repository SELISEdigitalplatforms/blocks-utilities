import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { CreateProjectResourcesForm } from "./create-project-resources-form";

const nextStep = vi.fn();
vi.mock("@/components/stepper/stepper-provider", () => ({
  useStepper: () => ({ nextStep }),
}));

const refetchAuthorization = vi.fn();
vi.mock("@/cross-modules/devops/hooks/github-info", () => ({
  useValidateAuthorization: () => ({ data: undefined, refetch: refetchAuthorization }),
  useGetRepositoryUser: () => ({ data: { login: "octo", name: "Octo Cat", avatar_url: "" } }),
}));

vi.mock("@/cross-modules/devops/components/deployment-steps/render-repos/render-provider", () => ({
  default: () => <div data-testid="provider-buttons" />,
}));

vi.mock("@/components/repository-selection-modal/repository-selection-modal", () => ({
  RepositorySelectionModal: ({
    open,
    onSelectRepository,
  }: {
    open: boolean;
    onSelectRepository: (r: unknown) => void;
  }) =>
    open ? (
      <button
        onClick={() =>
          onSelectRepository({
            id: 5,
            name: "repo",
            full_name: "octo/repo",
            html_url: "https://gh/repo",
          })
        }
      >
        select-repo
      </button>
    ) : null,
}));

const renderForm = () =>
  render(
    <MemoryRouter>
      <CreateProjectResourcesForm />
    </MemoryRouter>,
  );

describe("CreateProjectResourcesForm", () => {
  beforeEach(() => vi.clearAllMocks());

  it("renders the add-resource heading and the connected github account", () => {
    renderForm();
    expect(screen.getByText("Add resource")).toBeInTheDocument();
    expect(screen.getByText("octo")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /Add repository/ }),
    ).toBeInTheDocument();
  });

  it("opens the select modal when already authorized and adds a repo", async () => {
    refetchAuthorization.mockResolvedValue({ data: { isSuccess: true } });
    renderForm();
    fireEvent.click(screen.getByRole("button", { name: /Add repository/ }));
    const pick = await screen.findByText("select-repo");
    fireEvent.click(pick);
    await waitFor(() =>
      expect(screen.getByText(/octo\/repo/)).toBeInTheDocument(),
    );
  });

  it("opens the connect dialog when not authorized", async () => {
    refetchAuthorization.mockResolvedValue({ data: { isSuccess: false } });
    renderForm();
    fireEvent.click(screen.getByRole("button", { name: /Add repository/ }));
    expect(await screen.findByText("Connect repository")).toBeInTheDocument();
    expect(screen.getByTestId("provider-buttons")).toBeInTheDocument();
  });

  it("opens the connect dialog when the authorization check throws", async () => {
    refetchAuthorization.mockRejectedValue(new Error("boom"));
    renderForm();
    fireEvent.click(screen.getByRole("button", { name: /Add repository/ }));
    expect(await screen.findByText("Connect repository")).toBeInTheDocument();
  });
});
