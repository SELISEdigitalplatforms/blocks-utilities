import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { EnvironmentCard } from "./environment-card";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const QueryWrapper = createWrapper();

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

const navigate = vi.fn();
vi.mock("react-router", async () => {
  const actual =
    await vi.importActual<typeof import("react-router")>("react-router");
  return { ...actual, useNavigate: () => navigate };
});

const project = { tenantId: "t1", environment: "dev", tenantGroupId: "tg1" };

const renderCard = (props: Record<string, unknown> = {}) =>
  // The card starts impersonation through a react-query mutation, so it needs a client.
  render(
    <QueryWrapper>
      <MemoryRouter>
        <EnvironmentCard project={project as never} {...props} />
      </MemoryRouter>
    </QueryWrapper>,
  );

describe("EnvironmentCard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedProject: null });
  });

  it("renders the environment label", () => {
    renderCard();
    expect(screen.getByText("Development")).toBeInTheDocument();
  });

  it("impersonates and navigates when clicked", async () => {
    renderCard();
    fireEvent.click(screen.getByText("Development"));
    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/magic-url"));
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
    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/magic-url"));
  });
});
