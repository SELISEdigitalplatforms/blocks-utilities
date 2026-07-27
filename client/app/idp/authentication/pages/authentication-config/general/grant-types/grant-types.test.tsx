import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { GrantTypes } from "./grant-types";

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

describe("GrantTypes", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    configState = { data: { allowedGrantTypes: [] }, isLoading: false };
    useProjectStore.setState({ selectedProject: { tenantId: "tg-1" } });
  });

  it("shows the loading skeleton while the config loads", () => {
    configState = { data: undefined, isLoading: true };
    const { container } = render(<GrantTypes />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(
      0,
    );
  });

  it("renders a checkbox per grant type option", () => {
    render(<GrantTypes />);
    expect(screen.getByText("Email/Password")).toBeInTheDocument();
    expect(screen.getByText("SSO")).toBeInTheDocument();
    expect(screen.getByText("Client Credential")).toBeInTheDocument();
    expect(screen.getByText("Authorization Code")).toBeInTheDocument();
    expect(screen.getAllByRole("checkbox").length).toBe(4);
  });

  it("saves the selected grant types on submit", async () => {
    saveConfig.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    render(<GrantTypes />);

    await user.click(screen.getAllByRole("checkbox")[0]);
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    await user.click(save);

    await waitFor(() => expect(saveConfig).toHaveBeenCalled());
    expect(saveConfig.mock.calls[0][0].allowedGrantTypes.length).toBe(1);
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("shows an error toast when the save is unsuccessful", async () => {
    saveConfig.mockResolvedValue({ isSuccess: false, errors: "boom" });
    const user = userEvent.setup();
    render(<GrantTypes />);

    await user.click(screen.getAllByRole("checkbox")[0]);
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    await user.click(save);

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "boom" }),
    );
  });
});
