import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { SelfProject } from "./self-project";

let projectsState: { data: unknown; isLoading: boolean; isFetching: boolean };
vi.mock("@blocks-identifier/hooks/use-project", () => ({
  useGetProjects: () => projectsState,
}));

vi.mock("@/components/console-create/console-create", () => ({
  default: () => <div data-testid="console-create" />,
}));
vi.mock("@/components/project-card/project-card", () => ({
  ProjectCard: ({ project }: { project: { name: string } }) => (
    <div data-testid="project-card">{project?.name}</div>
  ),
}));
vi.mock("@/components/project-card/loading", () => ({
  ProjectCardLoading: () => <div data-testid="project-loading" />,
}));
vi.mock("framer-motion", () => ({
  motion: {
    div: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  },
}));

const makeGroups = (n: number) =>
  Array.from({ length: n }, (_, i) => ({
    tenantGroupId: `g${i}`,
    projects: [{ name: `Project ${i}` }],
  }));

describe("SelfProject", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    projectsState = { data: undefined, isLoading: false, isFetching: false };
  });

  it("shows the loading placeholders while fetching", () => {
    projectsState.isLoading = true;
    render(<SelfProject />);
    expect(screen.getAllByTestId("project-loading").length).toBe(8);
  });

  it("shows the create prompt when there are no project groups", () => {
    projectsState.data = [];
    render(<SelfProject />);
    expect(screen.getByTestId("console-create")).toBeInTheDocument();
  });

  it("renders a card per project group", () => {
    projectsState.data = makeGroups(3);
    render(<SelfProject />);
    expect(screen.getByText("Your Blocks Projects")).toBeInTheDocument();
    expect(screen.getAllByTestId("project-card")).toHaveLength(3);
  });

  it("shows the deletion hint when there are more than nine groups", () => {
    projectsState.data = makeGroups(10);
    render(<SelfProject />);
    expect(
      screen.getByText(/Please delete an existing project/),
    ).toBeInTheDocument();
  });
});
