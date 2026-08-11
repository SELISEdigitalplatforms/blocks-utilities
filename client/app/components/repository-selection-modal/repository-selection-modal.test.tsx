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
    fireEvent.click(screen.getByRole("button", { name: /Revoke repository access/ }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm" }));
    await waitFor(() => expect(revokeAccess).toHaveBeenCalled());
  });

  it("opens the revoke confirmation from the keyboard alone", async () => {
    const user = userEvent.setup();
    wrap(<RepositorySelectionModal {...baseProps} />);
    const revoke = screen.getByRole("button", { name: /Revoke repository access/ });
    revoke.focus();
    expect(revoke).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(await screen.findByRole("button", { name: "Confirm" })).toBeInTheDocument();
  });

  it("still closes the confirmation flow when revoking access fails", async () => {
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    revokeAccess.mockRejectedValue(new Error("revoke failed"));
    wrap(<RepositorySelectionModal {...baseProps} />);
    fireEvent.click(screen.getByRole("button", { name: /Revoke repository access/ }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm" }));
    await waitFor(() => expect(revokeAccess).toHaveBeenCalled());
    await waitFor(() =>
      expect(window.location.reload as unknown as ReturnType<typeof vi.fn>).toHaveBeenCalled(),
    );
    errorSpy.mockRestore();
  });

  it("shows the empty state when the response items are not an array", () => {
    reposResult = {
      data: { data: { items: null, total_count: 5 } },
      isLoading: false,
      isFetching: false,
    };
    wrap(<RepositorySelectionModal {...baseProps} />);
    expect(screen.getByText("Select repository")).toBeInTheDocument();
  });

  it("clears its state when the modal is closed", () => {
    repos([{ id: 1, full_name: "org/repo-1" }], 1);
    const client = new QueryClient();
    const { rerender } = render(
      <QueryClientProvider client={client}>
        <RepositorySelectionModal {...baseProps} />
      </QueryClientProvider>,
    );
    expect(screen.getByText("Select repository")).toBeInTheDocument();
    rerender(
      <QueryClientProvider client={client}>
        <RepositorySelectionModal {...baseProps} open={false} />
      </QueryClientProvider>,
    );
    expect(screen.queryByText("Select repository")).not.toBeInTheDocument();
  });

  it("filters via the search box", async () => {
    const user = userEvent.setup();
    repos([{ id: 1, full_name: "org/repo-1" }]);
    wrap(<RepositorySelectionModal {...baseProps} />);
    await user.click(screen.getByRole("combobox"));
    const search = await screen.findByPlaceholderText("Search repositories...");
    await user.type(search, "repo");
    expect((search as HTMLInputElement).value).toBe("repo");
  });

  it("reacts to scroll and wheel events on the repository list", async () => {
    const user = userEvent.setup();
    reposResult = {
      data: {
        data: {
          items: Array.from({ length: 10 }, (_, i) => ({
            id: i + 1,
            full_name: `org/repo-${i + 1}`,
          })),
          total_count: 25,
        },
      },
      isLoading: false,
      isFetching: false,
    };
    wrap(<RepositorySelectionModal {...baseProps} />);
    await user.click(screen.getByRole("combobox"));
    const item = (await screen.findAllByText("org/repo-1"))[0];
    const list = item.closest('[class*="overflow-y-auto"]') as HTMLElement;
    expect(list).toBeTruthy();
    fireEvent.scroll(list, { target: { scrollTop: 500 } });
    fireEvent.wheel(list, { deltaY: 120 });
    expect(screen.getAllByText("org/repo-1").length).toBeGreaterThan(0);
  });
});
