import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import React from "react";
import { MemoryRouter } from "react-router";
import { NuqsTestingAdapter } from "nuqs/adapters/testing";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { MagicUrlsList } from "./magic-urls-list";

const navigate = vi.fn();
vi.mock("react-router", async () => {
  const actual =
    await vi.importActual<typeof import("react-router")>("react-router");
  return { ...actual, useNavigate: () => navigate };
});

const deactivateMagicUrl = vi.fn();
let isRemoving = false;
vi.mock("@blocks-utilities/magic-url/hooks/use-deactivate-magic-url", () => ({
  useDeactivateMagicUrl: () => ({ deactivateMagicUrl, isRemoving }),
}));

const toast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  toast: (...a: unknown[]) => toast(...a),
}));

const row = (over: Record<string, unknown> = {}) => ({
  itemId: "m1",
  uri: "https://target.example/very/long/path",
  shortUri: "https://s.io/abc",
  name: "Promo",
  usageLimit: 0,
  expiryDate: "2025-01-02T00:00:00Z",
  status: 1,
  requestMethod: "get",
  clientCredential: "",
  ...over,
});

const renderList = (data: unknown[], loading = false) =>
  render(
    <MemoryRouter>
      <NuqsTestingAdapter>
        <MagicUrlsList data={data as never} isLoading={loading} />
      </NuqsTestingAdapter>
    </MemoryRouter>,
  );

describe("MagicUrlsList", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isRemoving = false;
    useProjectStore.setState({ selectedProject: { tenantId: "tg1" } });
  });

  it("renders the loading skeleton", () => {
    const { container } = renderList([], true);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("shows an empty state when there are no rows", () => {
    renderList([]);
    expect(screen.getByText("No results.")).toBeInTheDocument();
  });

  it("renders rows with formatted cells", () => {
    renderList([row()]);
    expect(screen.getByText("Promo")).toBeInTheDocument();
    expect(screen.getByText("Unlimited")).toBeInTheDocument();
    expect(screen.getByText("https://s.io/abc")).toBeInTheDocument();
  });

  it("navigates to details when a row is clicked", () => {
    renderList([row()]);
    fireEvent.click(screen.getByText("Promo"));
    expect(navigate).toHaveBeenCalledWith("/magic-url/details/m1");
  });

  it("opens the row menu and confirms deactivation", async () => {
    const user = userEvent.setup();
    deactivateMagicUrl.mockImplementation((_id, _tenant, cb) => cb());
    renderList([row()]);
    await user.click(document.querySelector("button.h-5.w-5.p-0") as Element);
    await user.click(await screen.findByText("Deactivate"));
    fireEvent.click(await screen.findByRole("button", { name: "Deactivate" }));
    await waitFor(() =>
      expect(deactivateMagicUrl).toHaveBeenCalledWith(
        "m1",
        "tg1",
        expect.any(Function),
      ),
    );
  });

  it("blocks deactivation and warns when no project is selected", async () => {
    const user = userEvent.setup();
    useProjectStore.setState({ selectedProject: null });
    renderList([row()]);
    await user.click(document.querySelector("button.h-5.w-5.p-0") as Element);
    await user.click(await screen.findByText("Deactivate"));
    fireEvent.click(await screen.findByRole("button", { name: "Deactivate" }));
    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "destructive" }),
      ),
    );
    expect(deactivateMagicUrl).not.toHaveBeenCalled();
  });
});
