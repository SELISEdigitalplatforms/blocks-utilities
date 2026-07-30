import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { useProjectStore } from "@seliseblocks/genesis-os";

let endpointsData: { data: unknown[] } | undefined = { data: [] };
let loading = false;
const updateEndpoint = vi.fn();
const bulkUpdate = vi.fn();

vi.mock("@blocks-idp/api-settings/hooks/use-api-settings", () => ({
  useGetApiEndpoints: () => ({ data: endpointsData, isLoading: loading }),
  useUpdateApiEndpoint: () => ({ mutateAsync: updateEndpoint }),
  useBulkUpdateApiEndpoints: () => ({ mutateAsync: bulkUpdate }),
}));

// Child components are stubbed to surface the callbacks the page wires up so the
// page's own handlers (toggle / bulk) can be exercised directly.
vi.mock("@blocks-idp/api-settings/components/service-group-card", () => ({
  ServiceGroupCard: ({
    controller,
    endpoints,
    onToggleMfa,
    onToggleCaptcha,
    onBulkGroupMfa,
    onBulkGroupCaptcha,
    onSelectEndpoint,
    onSelectGroup,
  }: {
    controller: string;
    endpoints: { itemId: string }[];
    onToggleMfa: (ep: unknown, v: boolean) => void;
    onToggleCaptcha: (ep: unknown, v: boolean) => void;
    onBulkGroupMfa: (ids: string[], v: boolean) => void;
    onBulkGroupCaptcha: (ids: string[], v: boolean) => void;
    onSelectEndpoint: (id: string, checked: boolean) => void;
    onSelectGroup: (ids: string[], checked: boolean) => void;
  }) => (
    <div data-testid={`group-${controller}`}>
      <button onClick={() => onToggleMfa(endpoints[0], true)}>mfa-{controller}</button>
      <button onClick={() => onToggleCaptcha(endpoints[0], true)}>captcha-{controller}</button>
      <button onClick={() => onBulkGroupMfa(endpoints.map((e) => e.itemId), true)}>
        bulk-mfa-{controller}
      </button>
      <button onClick={() => onBulkGroupCaptcha(endpoints.map((e) => e.itemId), true)}>
        bulk-captcha-{controller}
      </button>
      <button onClick={() => onSelectEndpoint(endpoints[0].itemId, true)}>
        select-{controller}
      </button>
      <button onClick={() => onSelectGroup(endpoints.map((e) => e.itemId), true)}>
        select-group-{controller}
      </button>
      <button onClick={() => onSelectGroup(endpoints.map((e) => e.itemId), false)}>
        deselect-group-{controller}
      </button>
    </div>
  ),
}));

vi.mock("@blocks-idp/api-settings/components/bulk-action-bar", () => ({
  BulkActionBar: ({
    selectedCount,
    onEnableMfa,
    onEnableCaptcha,
    onClear,
  }: {
    selectedCount: number;
    onEnableMfa: () => void;
    onEnableCaptcha: () => void;
    onClear: () => void;
  }) => (
    <div>
      <span data-testid="selected-count">{selectedCount}</span>
      <button onClick={onEnableMfa}>enable-mfa</button>
      <button onClick={onEnableCaptcha}>enable-captcha</button>
      <button onClick={onClear}>clear</button>
    </div>
  ),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

import ApiSettingsPage from "./api-settings";

const ep = (over: Record<string, unknown> = {}) => ({
  itemId: "e1",
  service: "iam",
  controller: "users",
  method: "get",
  endpoint: "/users",
  description: "list",
  baseUrl: "https://api.example.com",
  version: "v1",
  isMfaRequired: false,
  isCaptchaRequired: false,
  mfaType: 0,
  captchaProvider: 0,
  ...over,
});

describe("ApiSettingsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    endpointsData = { data: [] };
    loading = false;
    useProjectStore.setState({ selectedProject: { tenantId: "tg1" } });
  });

  it("shows skeletons while loading", () => {
    loading = true;
    const { container } = render(<ApiSettingsPage />);
    expect(screen.getByText("API Settings")).toBeInTheDocument();
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("shows the empty state when there are no endpoints", () => {
    render(<ApiSettingsPage />);
    expect(screen.getByText("No API endpoints configured.")).toBeInTheDocument();
  });

  it("groups endpoints by service and renders a swagger link", () => {
    endpointsData = { data: [ep(), ep({ itemId: "e2", method: "post" })] };
    render(<ApiSettingsPage />);
    expect(screen.getByText("iam")).toBeInTheDocument();
    expect(screen.getByTestId("group-users")).toBeInTheDocument();
    expect(
      screen.getByText(
        "https://api.example.com/iam/v1/swagger/v1/swagger.json",
      ),
    ).toBeInTheDocument();
  });

  it("toggles MFA for an endpoint and shows a success toast", async () => {
    endpointsData = { data: [ep()] };
    updateEndpoint.mockResolvedValue({ isSuccess: true });
    render(<ApiSettingsPage />);
    fireEvent.click(screen.getByText("mfa-users"));
    await waitFor(() =>
      expect(updateEndpoint).toHaveBeenCalledWith(
        expect.objectContaining({ isMfaRequired: true, projectKey: "tg1" }),
      ),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("surfaces an error toast when a toggle fails", async () => {
    endpointsData = { data: [ep()] };
    updateEndpoint.mockResolvedValue({ isSuccess: false, errors: ["nope"] });
    render(<ApiSettingsPage />);
    fireEvent.click(screen.getByText("captcha-users"));
    await waitFor(() => expect(showErrorToast).toHaveBeenCalled());
  });

  it("runs group and selection driven bulk updates", async () => {
    endpointsData = { data: [ep()] };
    bulkUpdate.mockResolvedValue({ isSuccess: true });
    render(<ApiSettingsPage />);

    fireEvent.click(screen.getByText("bulk-mfa-users"));
    await waitFor(() => expect(bulkUpdate).toHaveBeenCalled());

    fireEvent.click(screen.getByText("bulk-captcha-users"));

    fireEvent.click(screen.getByText("select-users"));
    await waitFor(() =>
      expect(screen.getByTestId("selected-count").textContent).toBe("1"),
    );

    fireEvent.click(screen.getByText("enable-mfa"));
    await waitFor(() =>
      expect(screen.getByTestId("selected-count").textContent).toBe("0"),
    );
  });

  it("enables captcha for the current selection", async () => {
    endpointsData = { data: [ep()] };
    bulkUpdate.mockResolvedValue({ isSuccess: true });
    render(<ApiSettingsPage />);
    fireEvent.click(screen.getByText("select-users"));
    fireEvent.click(screen.getByText("enable-captcha"));
    await waitFor(() =>
      expect(bulkUpdate).toHaveBeenCalledWith(
        expect.objectContaining({ isCaptchaRequired: true }),
      ),
    );
  });

  it("sorts multiple services, controllers and methods deterministically", () => {
    endpointsData = {
      data: [
        ep({ itemId: "b1", service: "zeta", controller: "beta", method: "post" }),
        ep({ itemId: "a1", service: "alpha", controller: "gamma", method: "delete" }),
        ep({ itemId: "a2", service: "alpha", controller: "gamma", method: "get" }),
        ep({ itemId: "a3", service: "alpha", controller: "delta", method: "get" }),
      ],
    };
    render(<ApiSettingsPage />);
    // alpha sorts before zeta.
    const headings = screen.getAllByRole("heading", { level: 2 });
    expect(headings[0]).toHaveTextContent("alpha");
    expect(headings[1]).toHaveTextContent("zeta");
  });

  it("opens the swagger UI in a new tab from the API Docs button", () => {
    endpointsData = { data: [ep()] };
    const openSpy = vi.spyOn(window, "open").mockImplementation(() => null);
    render(<ApiSettingsPage />);
    fireEvent.click(screen.getByRole("button", { name: /API Docs/i }));
    expect(openSpy).toHaveBeenCalledWith(
      "https://api.example.com/iam/v1/swagger/index.html",
      "_blank",
    );
    openSpy.mockRestore();
  });

  it("selects and deselects a whole group", async () => {
    endpointsData = { data: [ep(), ep({ itemId: "e2" })] };
    render(<ApiSettingsPage />);
    fireEvent.click(screen.getByText("select-group-users"));
    await waitFor(() =>
      expect(screen.getByTestId("selected-count").textContent).toBe("2"),
    );
    fireEvent.click(screen.getByText("deselect-group-users"));
    await waitFor(() =>
      expect(screen.getByTestId("selected-count").textContent).toBe("0"),
    );
  });

  it("surfaces an error when an MFA toggle is unsuccessful", async () => {
    endpointsData = { data: [ep()] };
    updateEndpoint.mockResolvedValue({ isSuccess: false, errors: ["mfa-bad"] });
    render(<ApiSettingsPage />);
    fireEvent.click(screen.getByText("mfa-users"));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "mfa-bad" }),
    );
  });

  it("surfaces an error when a group bulk MFA update fails", async () => {
    endpointsData = { data: [ep({ isCaptchaRequired: true })] };
    bulkUpdate.mockResolvedValue({ isSuccess: false, errors: ["grp-mfa"] });
    render(<ApiSettingsPage />);
    fireEvent.click(screen.getByText("bulk-mfa-users"));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "grp-mfa" }),
    );
  });

  it("surfaces an error when a group bulk Captcha update fails", async () => {
    endpointsData = { data: [ep({ isMfaRequired: true })] };
    bulkUpdate.mockResolvedValue({ isSuccess: false, errors: ["grp-cap"] });
    render(<ApiSettingsPage />);
    fireEvent.click(screen.getByText("bulk-captcha-users"));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "grp-cap" }),
    );
  });

  it("surfaces an error when the selection bulk MFA update fails", async () => {
    endpointsData = { data: [ep()] };
    bulkUpdate.mockResolvedValue({ isSuccess: false, errors: ["sel-mfa"] });
    render(<ApiSettingsPage />);
    fireEvent.click(screen.getByText("select-users"));
    fireEvent.click(screen.getByText("enable-mfa"));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "sel-mfa" }),
    );
  });

  it("surfaces an error when the selection bulk Captcha update fails", async () => {
    endpointsData = { data: [ep()] };
    bulkUpdate.mockResolvedValue({ isSuccess: false, errors: ["sel-cap"] });
    render(<ApiSettingsPage />);
    fireEvent.click(screen.getByText("select-users"));
    fireEvent.click(screen.getByText("enable-captcha"));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "sel-cap" }),
    );
  });
});
