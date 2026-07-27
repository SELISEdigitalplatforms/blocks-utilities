import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { ToggleCaptchaStatusModal } from "./toggle-captcha-status-modal";

const toggleStatus = vi.fn();
vi.mock("../hooks/use-captcha-config", () => ({
  useToggleCaptchaConfigStatus: () => ({
    mutateAsync: toggleStatus,
    isPending: false,
  }),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const enabledConfig = {
  itemId: "cap-1",
  provider: "recaptcha",
  isEnable: true,
} as never;

describe("ToggleCaptchaStatusModal", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedProject: { tenantId: "tg-1" } });
  });

  it("labels the trigger Disable when the captcha is enabled", () => {
    render(<ToggleCaptchaStatusModal configuration={enabledConfig} />);
    // DialogTrigger wraps the Button, producing a nested trigger button.
    expect(
      screen.getAllByRole("button", { name: /Disable/ }).length,
    ).toBeGreaterThan(0);
  });

  it("labels the trigger Enable when the captcha is disabled", () => {
    render(
      <ToggleCaptchaStatusModal
        configuration={{ ...enabledConfig, isEnable: false } as never}
      />,
    );
    expect(
      screen.getAllByRole("button", { name: /Enable/ }).length,
    ).toBeGreaterThan(0);
  });

  it("toggles the status and reports success", async () => {
    toggleStatus.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    render(<ToggleCaptchaStatusModal configuration={enabledConfig} />);

    await user.click(screen.getAllByRole("button", { name: /Disable/ })[0]);
    await user.click(await screen.findByRole("button", { name: "Yes" }));

    await waitFor(() =>
      expect(toggleStatus).toHaveBeenCalledWith({
        projectKey: "tg-1",
        isEnable: false,
        itemId: "cap-1",
      }),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("shows an error toast when the toggle is unsuccessful", async () => {
    toggleStatus.mockResolvedValue({ isSuccess: false, errors: "bad" });
    const user = userEvent.setup();
    render(<ToggleCaptchaStatusModal configuration={enabledConfig} />);

    await user.click(screen.getAllByRole("button", { name: /Disable/ })[0]);
    await user.click(await screen.findByRole("button", { name: "Yes" }));

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "bad" }),
    );
  });
});
