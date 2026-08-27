import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { ProjectCard } from "./project-card";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const navigate = vi.fn();
vi.mock("react-router", async () => {
  const actual =
    await vi.importActual<typeof import("react-router")>("react-router");
  return { ...actual, useNavigate: () => navigate };
});

// useStartImpersonation is a react-query mutation, so it is stubbed to keep the
// chip click deterministic and free of a QueryClientProvider. useScopedPath is
// left real, since the navigation target it builds is what the test asserts.
const startImpersonation = vi.fn();
vi.mock("@seliseblocks/genesis-os/hooks", async () => {
  const actual = await vi.importActual<
    typeof import("@seliseblocks/genesis-os/hooks")
  >("@seliseblocks/genesis-os/hooks");
  return {
    ...actual,
    useStartImpersonation: () => ({ mutateAsync: startImpersonation }),
  };
});

const project = (over: Record<string, unknown> = {}) => ({
  name: "My Project",
  tenantGroupId: "tg1",
  tenantId: "t1",
  environment: "dev",
  ...over,
});

// The card is rendered under /app/:itemId, the scope useScopedPath reads to
// build the environment chip's navigation target.
const renderCard = (projects: unknown[]) =>
  render(
    <MemoryRouter initialEntries={["/app/proj-1"]}>
      <Routes>
        <Route
          path="/app/:itemId"
          element={
            <ProjectCard
              project={project() as never}
              projects={projects as never}
            />
          }
        />
      </Routes>
    </MemoryRouter>,
  );

describe("ProjectCard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    startImpersonation.mockResolvedValue(undefined);
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
    await waitFor(() =>
      expect(navigate).toHaveBeenCalledWith("/app/proj-1/magic-url"),
    );
    expect(startImpersonation).toHaveBeenCalledWith({
      targeted_tenant_id: "env-t",
    });
    expect(useProjectStore.getState().selectedTenantGroup).toBe("env-tg");
  });
});
