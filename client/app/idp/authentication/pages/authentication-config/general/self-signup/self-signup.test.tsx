import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { SelfSignup } from "./self-signup";

let configState: { data: unknown; isLoading: boolean };
const saveConfig = vi.fn();
vi.mock("@blocks-idp/authentication/hooks/use-auth-config", () => ({
  useGetAuthConfig: () => configState,
  useSaveAuthConfig: () => ({ mutateAsync: saveConfig, isPending: false }),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

describe("SelfSignup", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    configState = { data: { isSelfSignUpAllowed: false }, isLoading: false };
    useProjectStore.setState({ selectedProject: { tenantId: "tg-1" } });
  });

  it("shows the loading skeleton while the config loads", () => {
    configState = { data: undefined, isLoading: true };
    const { container } = render(<SelfSignup />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(
      0,
    );
    expect(screen.queryByRole("button", { name: "Save" })).not.toBeInTheDocument();
  });

  it("renders the checkbox and disabled Save button when loaded", () => {
    render(<SelfSignup />);
    expect(screen.getByText("Allow Self Sign-Up")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  });

  it("saves the toggled value and reports success", async () => {
    saveConfig.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    render(<SelfSignup />);

    await user.click(screen.getByRole("checkbox"));
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    await user.click(save);

    await waitFor(() =>
      expect(saveConfig).toHaveBeenCalledWith(
        expect.objectContaining({
          isSelfSignUpAllowed: true,
          projectKey: "tg-1",
        }),
      ),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("shows an error toast when the save is unsuccessful", async () => {
    saveConfig.mockResolvedValue({ isSuccess: false, errors: "boom" });
    const user = userEvent.setup();
    render(<SelfSignup />);

    await user.click(screen.getByRole("checkbox"));
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    await user.click(save);

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "boom" }),
    );
  });
});
