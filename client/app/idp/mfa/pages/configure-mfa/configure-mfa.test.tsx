import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { ConfigureMFA } from "./configure-mfa";

let mfaResult: { isLoading: boolean; isFetching: boolean; data?: unknown } = {
  isLoading: false,
  isFetching: false,
  data: { userMfaType: [] },
};
const mutateAsync = vi.fn();
let isPending = false;
vi.mock("../../hooks/use-mfa-config", () => ({
  useGetMFAConfig: () => mfaResult,
  useSaveMFAConfig: () => ({ mutateAsync, isPending }),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

describe("ConfigureMFA", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
    mfaResult = { isLoading: false, isFetching: false, data: { userMfaType: [] } };
    useProjectStore.setState({ selectedProject: { tenantId: "tg1" } });
  });

  it("shows the loading skeleton", () => {
    mfaResult = { isLoading: true, isFetching: false, data: undefined };
    const { container } = render(<ConfigureMFA />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("shows the empty state when there is no config data", () => {
    mfaResult = { isLoading: false, isFetching: false, data: undefined };
    render(<ConfigureMFA />);
    expect(
      screen.getByText(/No configurations found/),
    ).toBeInTheDocument();
  });

  it("lists MFA providers with a Disabled status by default", () => {
    render(<ConfigureMFA />);
    expect(screen.getByText("Provider")).toBeInTheDocument();
    expect(screen.getAllByText("Disabled").length).toBeGreaterThan(0);
  });

  it("enables a provider through the confirmation modal", async () => {
    const user = userEvent.setup();
    mutateAsync.mockResolvedValue({ isSuccess: true });
    render(<ConfigureMFA />);
    const menuButtons = screen.getAllByRole("button");
    await user.click(menuButtons[0]);
    await user.click(await screen.findByText("Enable"));
    fireEvent.click(await screen.findByRole("button", { name: "Yes" }));
    await waitFor(() => expect(mutateAsync).toHaveBeenCalled());
    expect(mutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({ projectKey: "tg1", enableMfa: true }),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("surfaces an error toast when the save fails", async () => {
    const user = userEvent.setup();
    mutateAsync.mockResolvedValue({ isSuccess: false, errors: ["bad"] });
    render(<ConfigureMFA />);
    await user.click(screen.getAllByRole("button")[0]);
    await user.click(await screen.findByText("Enable"));
    fireEvent.click(await screen.findByRole("button", { name: "Yes" }));
    await waitFor(() => expect(showErrorToast).toHaveBeenCalledWith({ errors: ["bad"] }));
  });
});
