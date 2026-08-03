import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { ProjectList } from "./project-list";

let projectsResult: { data: { projects: unknown[] }[]; isLoading: boolean } = {
  data: [],
  isLoading: false,
};
let projectResult: { data: { data?: { name?: string } } } = { data: {} };

vi.mock("@blocks-identifier/hooks/use-project", () => ({
  useGetProjects: () => projectsResult,
  useGetProject: () => projectResult,
}));

const renderList = (collapsed = false) =>
  render(
    <MemoryRouter>
      <ProjectList collapsed={collapsed} />
    </MemoryRouter>,
  );

describe("ProjectList", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    projectsResult = { data: [], isLoading: false };
    projectResult = { data: {} };
    useProjectStore.setState({
      selectedProject: null,
      projects: [],
      selectedTenantGroup: null,
    });
  });

  it("shows the placeholder when no project is selected", () => {
    renderList();
    expect(screen.getByText("Select a Project")).toBeInTheDocument();
    expect(screen.getByText("Project")).toBeInTheDocument();
  });

  it("prefers the fetched project name over the stored selection", () => {
    useProjectStore.setState({ selectedProject: { itemId: "p1", name: "Stored" } });
    projectResult = { data: { data: { name: "Fetched" } } };
    renderList();
    expect(screen.getByText("Fetched")).toBeInTheDocument();
  });

  it("falls back to the selected project name", () => {
    useProjectStore.setState({ selectedProject: { itemId: "p1", name: "Stored" } });
    renderList();
    expect(screen.getByText("Stored")).toBeInTheDocument();
  });

  it("renders the collapsed trigger variant", () => {
    useProjectStore.setState({ selectedProject: { itemId: "p1", name: "Mine" } });
    renderList(true);
    expect(screen.getByText("Mine")).toBeInTheDocument();
  });
});
