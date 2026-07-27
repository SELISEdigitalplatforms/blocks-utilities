import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { EnvironmentsPage } from "./environments";

let projectsState: { data: unknown; isLoading: boolean; isFetching: boolean };
vi.mock("@/cross-modules/identifier/hooks/use-project", () => ({
  useGetProjects: () => projectsState,
}));

vi.mock("@/components/environment-card/add-environment-modal", () => ({
  AddEnvironmentModal: () => <div data-testid="add-env-modal" />,
}));

describe("EnvironmentsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedTenantGroup: "grp-1" });
    projectsState = { data: undefined, isLoading: false, isFetching: false };
  });

  it("shows the loading state while projects are loading", () => {
    projectsState.isLoading = true;
    const { container } = render(<EnvironmentsPage />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(
      0,
    );
    expect(screen.queryByText("Environments")).not.toBeInTheDocument();
  });

  it("shows the loading state when there are no projects yet", () => {
    projectsState.data = [{ projects: [] }];
    const { container } = render(<EnvironmentsPage />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(
      0,
    );
  });

  it("renders the environments heading and shared section when loaded", () => {
    projectsState.data = [
      {
        isShared: true,
        projects: [
          { itemId: "p1", name: "Env One", environment: "prod" },
          { itemId: "p2", name: "Env Two", environment: "dev" },
        ],
        nonSharedProject: [],
      },
    ];
    render(<EnvironmentsPage />);
    expect(screen.getByText("Environments")).toBeInTheDocument();
    expect(screen.getByText("Shared with you")).toBeInTheDocument();
  });

  it("renders the others section for non-shared projects", () => {
    projectsState.data = [
      {
        isShared: true,
        projects: [{ itemId: "p1", name: "Env One", environment: "prod" }],
        nonSharedProject: [
          { itemId: "p3", name: "Env Three", environment: "stage" },
        ],
      },
    ];
    render(<EnvironmentsPage />);
    expect(screen.getByText("Others")).toBeInTheDocument();
  });
});
