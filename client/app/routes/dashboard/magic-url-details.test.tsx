import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route } from "react-router";
import { useProjectStore } from "@seliseblocks/genesis-os";
import MagicUrlDetailsPage from "./magic-url-details";

const navigate = vi.fn();
vi.mock("react-router", async () => {
  const actual =
    await vi.importActual<typeof import("react-router")>("react-router");
  return { ...actual, useNavigate: () => navigate };
});

let magicUrlResult: { data?: unknown; isLoading: boolean; isError: boolean } = {
  data: undefined,
  isLoading: false,
  isError: false,
};
vi.mock("@blocks-utilities/magic-url/hooks/use-magic-url", () => ({
  useGetMagicUrlById: () => magicUrlResult,
}));

const deactivateMagicUrl = vi.fn();
let isRemoving = false;
vi.mock("@blocks-utilities/magic-url/hooks/use-deactivate-magic-url", () => ({
  useDeactivateMagicUrl: () => ({ deactivateMagicUrl, isRemoving }),
}));
vi.mock("@/cross-modules/utilities/magic-url/hooks/use-user-details", () => ({
  useGetCreator: () => ({ data: { data: { firstName: "Ada", lastName: "Lovelace" } } }),
}));
vi.mock("@/contexts/breadcrumb-context", () => ({
  useDynamicBreadcrumbLabel: () => {},
}));
vi.mock("@/components/breadcrumb/breadcrumb", () => ({
  default: () => <div data-testid="breadcrumb" />,
}));

const toast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({ toast: (...a: unknown[]) => toast(...a) }));

const magicUrl = (over: Record<string, unknown> = {}) => ({
  itemId: "m1",
  name: "Promo",
  usageCount: 2,
  usageLimit: 10,
  status: 1,
  createdAt: "2024-01-01T00:00:00Z",
  expiryDate: "2025-01-01T00:00:00Z",
  shortUri: "https://s.io/abc",
  uri: "https://target.example",
  createdBy: "u1",
  ...over,
});

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={["/magic-url/details/m1"]}>
      <Routes>
        <Route path="/magic-url/details/:id" element={<MagicUrlDetailsPage />} />
      </Routes>
    </MemoryRouter>,
  );

describe("MagicUrlDetailsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isRemoving = false;
    magicUrlResult = { data: undefined, isLoading: false, isError: false };
    useProjectStore.setState({ selectedProject: { tenantId: "tg1" } });
  });

  it("renders the skeleton while loading", () => {
    magicUrlResult = { data: undefined, isLoading: true, isError: false };
    const { container } = renderPage();
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("renders an error state", () => {
    magicUrlResult = { data: undefined, isLoading: false, isError: true };
    renderPage();
    expect(screen.getByText("Error loading details")).toBeInTheDocument();
  });

  it("renders a not-found state when there is no data", () => {
    magicUrlResult = { data: undefined, isLoading: false, isError: false };
    renderPage();
    expect(screen.getByText("Details not found")).toBeInTheDocument();
  });

  it("renders the details with the resolved creator name", () => {
    magicUrlResult = { data: magicUrl(), isLoading: false, isError: false };
    renderPage();
    expect(screen.getByRole("heading", { name: "Promo" })).toBeInTheDocument();
    expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByText("https://s.io/abc")).toBeInTheDocument();
  });

  it("deactivates from the row menu and navigates back", async () => {
    const user = userEvent.setup();
    deactivateMagicUrl.mockImplementation((_id, _t, cb) => cb());
    magicUrlResult = { data: magicUrl(), isLoading: false, isError: false };
    renderPage();
    await user.click(document.querySelector("button.h-8.w-8.p-0") as Element);
    await user.click(await screen.findByText("Deactivate"));
    fireEvent.click(await screen.findByRole("button", { name: "Deactivate" }));
    await waitFor(() =>
      expect(deactivateMagicUrl).toHaveBeenCalledWith("m1", "tg1", expect.any(Function)),
    );
    expect(navigate).toHaveBeenCalledWith("/magic-url");
  });
});
