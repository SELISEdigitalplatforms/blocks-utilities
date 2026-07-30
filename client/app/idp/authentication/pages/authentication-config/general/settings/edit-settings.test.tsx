import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { EditGeneralSettings } from "./edit-settings";

let configData: unknown;
const saveConfig = vi.fn();
vi.mock("@blocks-idp/authentication/hooks/use-auth-config", () => ({
  useGetAuthConfig: () => ({ data: configData }),
  useSaveAuthConfig: () => ({ mutateAsync: saveConfig, isPending: false }),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const fullConfig = {
  refreshTokenValidForNumberMinutes: 60,
  getNumberOfWrongAttemptsToLockTheAccount: 5,
  accountLockDurationInMinutes: 15,
  accessTokenValidForNumberMinutes: 30,
  rememberMeRefreshTokenValidForNumberMinutes: 120,
};

describe("EditGeneralSettings", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    configData = { ...fullConfig };
    useProjectStore.setState({ selectedProject: { tenantId: "tg-1" } });
  });

  it("opens the settings dialog from the Edit trigger", async () => {
    const user = userEvent.setup();
    render(<EditGeneralSettings />);
    await user.click(screen.getByRole("button", { name: "Edit" }));
    expect(await screen.findByText("Settings")).toBeInTheDocument();
    expect(
      screen.getByText("Access Token Validity (minutes)"),
    ).toBeInTheDocument();
  });

  it("saves the updated configuration and reports success", async () => {
    saveConfig.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    render(<EditGeneralSettings />);

    await user.click(screen.getByRole("button", { name: "Edit" }));
    await screen.findByText("Settings");

    const inputs = screen.getAllByPlaceholderText("Set a duration in minutes");
    await user.clear(inputs[0]);
    await user.type(inputs[0], "45");

    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    await user.click(save);

    await waitFor(() => expect(saveConfig).toHaveBeenCalled());
    expect(saveConfig.mock.calls[0][0]).toMatchObject({ projectKey: "tg-1" });
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("shows an error toast when the save is unsuccessful", async () => {
    saveConfig.mockResolvedValue({ isSuccess: false, errors: "boom" });
    const user = userEvent.setup();
    render(<EditGeneralSettings />);

    await user.click(screen.getByRole("button", { name: "Edit" }));
    await screen.findByText("Settings");

    const inputs = screen.getAllByPlaceholderText("Set a duration in minutes");
    await user.clear(inputs[0]);
    await user.type(inputs[0], "45");

    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    await user.click(save);

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "boom" }),
    );
  });
});
