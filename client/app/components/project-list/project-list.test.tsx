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

  it("prefers the freshly fetched project name over the stored one", () => {
    useProjectStore.setState({
      selectedProject: { itemId: "p1", name: "Stale name" },
      projects: [],
      selectedTenantGroup: null,
    });
    projectResult = { data: { data: { name: "Fresh name" } } };

    renderList();

    expect(screen.getByText("Fresh name")).toBeInTheDocument();
    expect(screen.queryByText("Stale name")).not.toBeInTheDocument();
  });

  it("falls back to the stored name until the fetch returns", () => {
    useProjectStore.setState({
      selectedProject: { itemId: "p1", name: "Stored name" },
      projects: [],
      selectedTenantGroup: null,
    });
    projectResult = { data: {} };

    renderList();

    expect(screen.getByText("Stored name")).toBeInTheDocument();
  });

  it("flattens the projects out of their groups", () => {
    projectsResult = {
      data: [
        { projects: [{ itemId: "a", name: "Alpha" }] },
        { projects: [{ itemId: "b", name: "Beta" }] },
      ],
      isLoading: false,
    };
    useProjectStore.setState({
      selectedProject: { itemId: "a", name: "Alpha" },
      projects: [],
      selectedTenantGroup: null,
    });

    renderList();

    expect(screen.getByText("Alpha")).toBeInTheDocument();
  });

  it("drops empty entries a group may carry", () => {
    projectsResult = {
      data: [{ projects: [null, { itemId: "a", name: "Alpha" }] }],
      isLoading: false,
    };
    useProjectStore.setState({
      selectedProject: { itemId: "a", name: "Alpha" },
      projects: [],
      selectedTenantGroup: null,
    });

    const act = () => renderList();

    expect(act).not.toThrow();
  });

  it("uses the stored projects when the query returns none", () => {
    projectsResult = { data: [], isLoading: false };
    useProjectStore.setState({
      selectedProject: { itemId: "s1", name: "Stored only" },
      projects: [{ itemId: "s1", name: "Stored only" }],
      selectedTenantGroup: null,
    });

    renderList();

    expect(screen.getByText("Stored only")).toBeInTheDocument();
  });

  it("shows the name in the collapsed tooltip too", () => {
    useProjectStore.setState({
      selectedProject: { itemId: "p1", name: "Collapsed name" },
      projects: [],
      selectedTenantGroup: null,
    });

    renderList(true);

    expect(screen.getByText("Collapsed name")).toBeInTheDocument();
  });

});
