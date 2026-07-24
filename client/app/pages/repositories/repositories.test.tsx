import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { RepositoriesPage } from "./repositories";

let assetsResult: {
  data?: unknown;
  isLoading: boolean;
  isFetching: boolean;
  refetch: () => void;
} = { data: { assets: { resources: [] }, totalCount: 0 }, isLoading: false, isFetching: false, refetch: vi.fn() };
const addMutateAsync = vi.fn();
const refetchAuthorization = vi.fn();
vi.mock("@blocks-identifier/hooks/use-project", () => ({
  useGetAssets: () => assetsResult,
  useAddAssets: () => ({ mutateAsync: addMutateAsync }),
}));
vi.mock("@/cross-modules/devops/hooks/github-info", () => ({
  useValidateAuthorization: () => ({ data: undefined, refetch: refetchAuthorization }),
}));

// Heavy children are stubbed; the page's own logic is the unit under test.
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
          onSelectRepository({ id: 9, full_name: "org/new", html_url: "https://gh/new" })
        }
      >
        pick-repo
      </button>
    ) : null,
}));
vi.mock("@/cross-modules/devops/components/deployment-steps/render-repos/render-provider", () => ({
  default: () => <div data-testid="provider-buttons" />,
}));

const toast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({ toast: (...a: unknown[]) => toast(...a) }));

describe("RepositoriesPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    assetsResult = {
      data: { assets: { resources: [] }, totalCount: 0 },
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    };
    useProjectStore.setState({ selectedTenantGroup: "tg1" });
  });

  it("shows the empty state", () => {
    render(<RepositoriesPage />);
    expect(screen.getByText("Repositories")).toBeInTheDocument();
    expect(
      screen.getByText("No repositories found. Add a repository to get started."),
    ).toBeInTheDocument();
  });

  it("renders resource rows", () => {
    assetsResult = {
      data: {
        assets: { resources: [{ name: "repo-1", link: "https://gh/1", resourceId: "1" }] },
        totalCount: 1,
      },
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    };
    render(<RepositoriesPage />);
    expect(screen.getByText("repo-1")).toBeInTheDocument();
    expect(screen.getByText("https://gh/1")).toBeInTheDocument();
  });

  it("shows loading skeletons while fetching", () => {
    assetsResult = {
      data: { assets: { resources: [] }, totalCount: 0 },
      isLoading: false,
      isFetching: true,
      refetch: vi.fn(),
    };
    const { container } = render(<RepositoriesPage />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("opens the select modal when already authorized and adds a repo", async () => {
    refetchAuthorization.mockResolvedValue({ data: { isSuccess: true } });
    addMutateAsync.mockResolvedValue({});
    render(<RepositoriesPage />);
    fireEvent.click(screen.getByRole("button", { name: /Add/ }));
    const pick = await screen.findByText("pick-repo");
    fireEvent.click(pick);
    await waitFor(() => expect(addMutateAsync).toHaveBeenCalled());
    expect(addMutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({ tenantGroupId: "tg1" }),
    );
    expect(toast).toHaveBeenCalledWith(
      expect.objectContaining({ variant: "success" }),
    );
  });

  it("opens the provider connect dialog when not authorized", async () => {
    refetchAuthorization.mockResolvedValue({ data: { isSuccess: false } });
    render(<RepositoriesPage />);
    fireEvent.click(screen.getByRole("button", { name: /Add/ }));
    expect(await screen.findByText("Connect repository")).toBeInTheDocument();
    expect(screen.getByTestId("provider-buttons")).toBeInTheDocument();
  });
});
