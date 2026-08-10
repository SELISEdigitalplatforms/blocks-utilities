import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { profileMfaContext } from "../profile-mfa";

const mutateAsync = vi.fn();
let isPending = false;
vi.mock("@/idp/mfa/hooks/use-mfa-config", () => ({
  useDisableMfa: () => ({ isPending, mutateAsync }),
}));

const showErrorToast = vi.fn();
const showSuccessToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (args: unknown) => showErrorToast(args),
  showSuccessToast: (args: unknown) => showSuccessToast(args),
}));

import { UserMFAConfirmationDisable } from "./profile-mfa-confirmation-disable";

const setIsDisableModalOpen = vi.fn();

const renderDialog = (open = true) =>
  render(
    <profileMfaContext.Provider
      value={
        {
          userId: "u1",
          projectKey: "tg-1",
          isDisableModalOpen: open,
          setIsDisableModalOpen,
        } as never
      }
    >
      <UserMFAConfirmationDisable />
    </profileMfaContext.Provider>,
  );

const confirm = () => screen.getByRole("button", { name: "Yes" });

describe("UserMFAConfirmationDisable", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
    mutateAsync.mockResolvedValue({ isSuccess: true });
  });

  it("should warn that disabling reduces account security", () => {
    renderDialog();

    expect(screen.getByText("Disable MFA?")).toBeTruthy();
    expect(screen.getByText(/may reduce the security of this account/)).toBeTruthy();
  });

  it("should render nothing while closed", () => {
    renderDialog(false);

    expect(screen.queryByText("Disable MFA?")).toBeNull();
  });

  it("should disable MFA for the user in the current project and close on success", async () => {
    renderDialog();

    await userEvent.click(confirm());

    expect(mutateAsync).toHaveBeenCalledWith({ projectKey: "tg-1", userId: "u1" });
    expect(showSuccessToast).toHaveBeenCalledWith({
      description: "MFA disabled successfully",
    });
    expect(setIsDisableModalOpen).toHaveBeenCalledWith(false);
  });

  it("should keep the dialog open when the server refuses", async () => {
    // A refusal comes back as a normal response, not a rejection, so closing here would
    // tell the user MFA is off when it is still on.
    mutateAsync.mockResolvedValue({ isSuccess: false, errors: { mfa: ["still enrolled"] } });
    renderDialog();

    await userEvent.click(confirm());

    expect(showErrorToast).toHaveBeenCalledWith({ errors: { mfa: ["still enrolled"] } });
    expect(showSuccessToast).not.toHaveBeenCalled();
    expect(setIsDisableModalOpen).not.toHaveBeenCalled();
  });

  it("should report a thrown error that carries field errors", async () => {
    mutateAsync.mockRejectedValue({ errors: { mfa: ["network"] } });
    renderDialog();

    await userEvent.click(confirm());

    expect(showErrorToast).toHaveBeenCalledWith({ errors: { mfa: ["network"] } });
    expect(setIsDisableModalOpen).not.toHaveBeenCalled();
  });

  it("should swallow a thrown error with no field errors rather than surfacing an empty toast", async () => {
    mutateAsync.mockRejectedValue(new Error("boom"));
    renderDialog();

    await userEvent.click(confirm());

    expect(showErrorToast).not.toHaveBeenCalled();
    expect(showSuccessToast).not.toHaveBeenCalled();
  });

  it("should block both buttons while the request is in flight", () => {
    isPending = true;
    renderDialog();

    expect((screen.getByRole("button", { name: "Processing" }) as HTMLButtonElement).disabled).toBe(
      true,
    );
    expect((screen.getByRole("button", { name: "Cancel" }) as HTMLButtonElement).disabled).toBe(
      true,
    );
  });
});
