import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { SSoProviderStatusToggle } from "./sso-provider-status-toggle";

const updateStatus = vi.fn();
vi.mock("@blocks-idp/authentication/hooks/use-sso", () => ({
  useUpdateSsoCredentialStatus: () => ({ mutateAsync: updateStatus }),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const config = { itemId: "sso-1", isDisabled: false } as never;

describe("SSoProviderStatusToggle", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedProject: { tenantId: "tg-1" } });
  });

  it("shows the disable prompt when the provider is enabled", () => {
    render(
      <SSoProviderStatusToggle open setOpen={vi.fn()} configuration={config} />,
    );
    expect(screen.getByText("Disable")).toBeInTheDocument();
    expect(
      screen.getByText(/Are you sure you want to disable this provider/),
    ).toBeInTheDocument();
  });

  it("shows the enable prompt when the provider is disabled", () => {
    render(
      <SSoProviderStatusToggle
        open
        setOpen={vi.fn()}
        configuration={{ itemId: "sso-1", isDisabled: true } as never}
      />,
    );
    expect(screen.getByText("Enable")).toBeInTheDocument();
    expect(
      screen.getByText(/Are you sure you want to enable this provider/),
    ).toBeInTheDocument();
  });

  it("updates the status and closes on success", async () => {
    updateStatus.mockResolvedValue({ isSuccess: true });
    const setOpen = vi.fn();
    const user = userEvent.setup();
    render(
      <SSoProviderStatusToggle open setOpen={setOpen} configuration={config} />,
    );

    await user.click(screen.getByRole("button", { name: "Yes" }));

    await waitFor(() =>
      expect(updateStatus).toHaveBeenCalledWith({
        itemId: "sso-1",
        projectKey: "tg-1",
        isEnabled: true,
      }),
    );
    expect(showSuccessToast).toHaveBeenCalled();
    expect(setOpen).toHaveBeenCalledWith(false);
  });

  it("shows an error toast when the update is unsuccessful", async () => {
    updateStatus.mockResolvedValue({ isSuccess: false, errors: "boom" });
    const user = userEvent.setup();
    render(
      <SSoProviderStatusToggle open setOpen={vi.fn()} configuration={config} />,
    );

    await user.click(screen.getByRole("button", { name: "Yes" }));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "boom" }),
    );
  });
});
