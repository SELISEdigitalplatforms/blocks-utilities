import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RepositorySelectionModal } from "./repository-selection-modal";

let reposResult: { data?: unknown; isLoading: boolean; isFetching: boolean } = {
  data: { data: { items: [], total_count: 0 } },
  isLoading: false,
  isFetching: false,
};
vi.mock("@/cross-modules/devops/hooks/github-info", () => ({
  useGetGithubRepos: () => reposResult,
}));

const revokeAccess = vi.fn();
vi.mock("@/cross-modules/devops/services/github-info.service", () => ({
  githubInfoService: { revokeAccess: () => revokeAccess() },
}));

const wrap = (ui: React.ReactElement) =>
  render(
    <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>,
  );

const repos = (items: unknown[], total = items.length) => {
  reposResult = {
    data: { data: { items, total_count: total } },
    isLoading: false,
    isFetching: false,
  };
};

beforeEach(() => {
  vi.clearAllMocks();
  repos([]);
  Object.defineProperty(window, "location", {
    configurable: true,
    value: { ...window.location, reload: vi.fn() },
  });
});

describe("RepositorySelectionModal", () => {
  const baseProps = {
    open: true,
    onOpenChange: vi.fn(),
    onSelectRepository: vi.fn(),
  };

  it("renders the title, provider row and result count", () => {
    repos([{ id: 1, full_name: "org/repo-1" }], 1);
    wrap(<RepositorySelectionModal {...baseProps} />);
    expect(screen.getByText("Select repository")).toBeInTheDocument();
    expect(screen.getByText("GitHub")).toBeInTheDocument();
    expect(screen.getByText(/1 results/)).toBeInTheDocument();
  });

  it("selects a repository and calls onSelectRepository on Add", async () => {
    const user = userEvent.setup();
    const onSelectRepository = vi.fn();
    repos([
      { id: 1, full_name: "org/repo-1" },
      { id: 2, full_name: "org/repo-2" },
    ]);
    wrap(
      <RepositorySelectionModal
        {...baseProps}
        onSelectRepository={onSelectRepository}
      />,
    );
    await user.click(screen.getByRole("combobox"));
    await user.click(await screen.findByText("org/repo-2"));
    const add = screen.getByRole("button", { name: "Add" });
    await waitFor(() => expect(add).toBeEnabled());
    await user.click(add);
    expect(onSelectRepository).toHaveBeenCalledWith(
      expect.objectContaining({ id: 2 }),
    );
  });

  it("flags a repository that is already selected", async () => {
    const user = userEvent.setup();
    repos([{ id: 1, full_name: "org/repo-1" }]);
    wrap(
      <RepositorySelectionModal
        {...baseProps}
        selectedRepositories={[{ id: 1, full_name: "org/repo-1" } as never]}
      />,
    );
    await user.click(screen.getByRole("combobox"));
    await user.click(await screen.findByText("org/repo-1"));
    await user.click(screen.getByRole("button", { name: "Add" }));
    expect(screen.getByText("Repository already selected.")).toBeInTheDocument();
  });

  it("cancels via the Cancel button", () => {
    const onOpenChange = vi.fn();
    wrap(<RepositorySelectionModal {...baseProps} onOpenChange={onOpenChange} />);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("revokes github access from the confirmation modal", async () => {
    revokeAccess.mockResolvedValue(undefined);
    wrap(<RepositorySelectionModal {...baseProps} />);
    fireEvent.click(screen.getByText("Revoke repository access"));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm" }));
    await waitFor(() => expect(revokeAccess).toHaveBeenCalled());
  });
});
