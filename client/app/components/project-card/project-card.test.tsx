import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { ProjectCard } from "./project-card";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const navigate = vi.fn();
vi.mock("react-router", async () => {
  const actual =
    await vi.importActual<typeof import("react-router")>("react-router");
  return { ...actual, useNavigate: () => navigate };
});

// Switching environment impersonates the target tenant before navigating. Left real, the
// mutation reaches the network, rejects under jsdom, and the navigation never happens.
vi.mock("@seliseblocks/genesis-os/hooks", async () => {
  const actual = await vi.importActual<
    typeof import("@seliseblocks/genesis-os/hooks")
  >("@seliseblocks/genesis-os/hooks");
  return {
    ...actual,
    useStartImpersonation: () => ({
      mutateAsync: vi.fn().mockResolvedValue({}),
    }),
  };
});

const project = (over: Record<string, unknown> = {}) => ({
  name: "My Project",
  tenantGroupId: "tg1",
  tenantId: "t1",
  environment: "dev",
  ...over,
});

// The card starts impersonation through a react-query mutation, so it needs a client.
const renderCard = (projects: unknown[]) => {
  const Wrapper = createWrapper();

  return render(
    <Wrapper>
      <MemoryRouter>
        <ProjectCard
          project={project() as never}
          projects={projects as never}
        />
      </MemoryRouter>
    </Wrapper>,
  );
};

describe("ProjectCard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedProject: null, selectedTenantGroup: null });
  });

  it("renders the project name and a configure action", () => {
    renderCard([project()]);
    expect(screen.getByText("My Project")).toBeInTheDocument();
    expect(screen.getByText("Development")).toBeInTheDocument();
  });

  it("navigates to environments on configure", () => {
    renderCard([project()]);
    // The configure button is the first button rendered in the card header.
    fireEvent.click(screen.getAllByRole("button")[0]);
    expect(navigate).toHaveBeenCalledWith("/app/project/tg1/environments");
    expect(useProjectStore.getState().selectedTenantGroup).toBe("tg1");
  });

  it("shows a no-environments message when the list is empty", () => {
    renderCard([]);
    expect(screen.getByText("No environments")).toBeInTheDocument();
  });

  it("collapses environments beyond three into a +N chip", () => {
    renderCard([
      project({ environment: "dev" }),
      project({ environment: "test" }),
      project({ environment: "stg" }),
      project({ environment: "uat" }),
    ]);
    expect(screen.getByText("+1")).toBeInTheDocument();
  });

  it("impersonates and navigates when an environment chip is clicked", async () => {
    renderCard([project({ environment: "dev", tenantId: "env-t", tenantGroupId: "env-tg" })]);
    fireEvent.click(screen.getByText("Development"));
    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/magic-url"));
  });
});
