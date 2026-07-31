import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { EnvironmentCard } from "./environment-card";

const navigate = vi.fn();
vi.mock("react-router", async () => {
  const actual =
    await vi.importActual<typeof import("react-router")>("react-router");
  return { ...actual, useNavigate: () => navigate };
});

// useStartImpersonation is a react-query mutation, so it is stubbed to keep the
// click flow deterministic and free of a QueryClientProvider. useScopedPath is
// left real, since the navigation target it builds is what the tests assert.
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

const project = { tenantId: "t1", environment: "dev", tenantGroupId: "tg1" };

// The card is rendered under /app/:itemId, the scope useScopedPath reads to
// build its navigation target.
const renderCard = (props: Record<string, unknown> = {}) =>
  render(
    <MemoryRouter initialEntries={["/app/proj-1"]}>
      <Routes>
        <Route
          path="/app/:itemId"
          element={<EnvironmentCard project={project as never} {...props} />}
        />
      </Routes>
    </MemoryRouter>,
  );

describe("EnvironmentCard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    startImpersonation.mockResolvedValue(undefined);
    useProjectStore.setState({ selectedProject: null });
  });

  it("renders the environment label", () => {
    renderCard();
    expect(screen.getByText("Development")).toBeInTheDocument();
  });

  it("impersonates and navigates when clicked", async () => {
    renderCard();
    fireEvent.click(screen.getByText("Development"));
    await waitFor(() =>
      expect(navigate).toHaveBeenCalledWith("/app/proj-1/magic-url"),
    );
    expect(startImpersonation).toHaveBeenCalledWith({
      targeted_tenant_id: "t1",
    });
    expect(useProjectStore.getState().selectedProject).toEqual(project);
  });

  it("opens a confirmation when migration is ongoing", async () => {
    renderCard({ isMigrationOngoing: true });
    fireEvent.click(screen.getByText("Development"));
    expect(
      await screen.findByText("Environment Migration in Progress"),
    ).toBeInTheDocument();
    expect(navigate).not.toHaveBeenCalled();
  });

  it("proceeds after confirming the migration warning", async () => {
    renderCard({ isMigrationOngoing: true });
    fireEvent.click(screen.getByText("Development"));
    fireEvent.click(await screen.findByRole("button", { name: "Continue Anyway" }));
    await waitFor(() =>
      expect(navigate).toHaveBeenCalledWith("/app/proj-1/magic-url"),
    );
  });
});
