import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { profileMfaContext } from "../../profile-mfa";
import { ProfileMfaVerifyForm } from "./profile-mfa-verify-form";

const verifyOtp = vi.fn();
let isPending = false;
vi.mock("@/idp/mfa/hooks/use-mfa-config", () => ({
  useVerifyMfaOTP: () => ({ mutateAsync: verifyOtp, isPending }),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const setIsVerifyModalOpen = vi.fn();

const renderForm = (mfaMethodType = 2) =>
  render(
    <Dialog open>
      <profileMfaContext.Provider
        value={{
          projectKey: "p1",
          userId: "u1",
          isVerifyModalOpen: true,
          setIsVerifyModalOpen,
          isDisableModalOpen: false,
          setIsDisableModalOpen: vi.fn(),
          showVerifyModal: vi.fn(),
          mfaMethodType,
        }}
      >
        <ProfileMfaVerifyForm mfaId="mfa-1" />
      </profileMfaContext.Provider>
    </Dialog>,
  );

const typeCode = async (user: ReturnType<typeof userEvent.setup>, code: string) => {
  const input = document.querySelector("input") as HTMLInputElement;
  await user.type(input, code);
};

describe("ProfileMfaVerifyForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
  });

  it("closes the modal and toasts success on a valid verification", async () => {
    verifyOtp.mockResolvedValue({ isSuccess: true, isValid: true });
    const user = userEvent.setup();
    renderForm(2);

    await typeCode(user, "12345");
    await user.click(screen.getByRole("button", { name: "Verify" }));

    await waitFor(() =>
      expect(verifyOtp).toHaveBeenCalledWith(
        expect.objectContaining({ mfaId: "mfa-1", verificationCode: "12345", projectKey: "p1" }),
      ),
    );
    expect(setIsVerifyModalOpen).toHaveBeenCalledWith(false);
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("toasts an error when the response is unsuccessful", async () => {
    verifyOtp.mockResolvedValue({ isSuccess: false, errors: "boom" });
    const user = userEvent.setup();
    renderForm(2);

    await typeCode(user, "12345");
    await user.click(screen.getByRole("button", { name: "Verify" }));

    await waitFor(() => expect(showErrorToast).toHaveBeenCalledWith({ errors: "boom" }));
    expect(showSuccessToast).not.toHaveBeenCalled();
  });

  it("toasts an error when the code is not valid", async () => {
    verifyOtp.mockResolvedValue({ isSuccess: true, isValid: false, errors: "" });
    const user = userEvent.setup();
    renderForm(2);

    await typeCode(user, "12345");
    await user.click(screen.getByRole("button", { name: "Verify" }));

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "Code is not valid" }),
    );
  });

  it("closes the modal via the cancel button", async () => {
    const user = userEvent.setup();
    renderForm(1);
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    expect(setIsVerifyModalOpen).toHaveBeenCalledWith(false);
  });
});
