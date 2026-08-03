import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DialogTrigger } from "@/components/ui-kits/dialog/dialog";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { ConfigureCaptchaModal } from "./configure-captcha-modal";

let configsResult: { isLoading: boolean; isFetching: boolean; data?: unknown } = {
  isLoading: false,
  isFetching: false,
  data: { configurations: [] },
};
const mutateAsync = vi.fn();
let isPending = false;
vi.mock("../../hooks/use-captcha-config", () => ({
  useGetCaptchaConfigs: () => configsResult,
  useSaveCaptcha: () => ({ mutateAsync, isPending }),
}));

// The nested field components render their own inputs; stub them so the modal
// itself is the unit under test.
vi.mock("./configure-general-captcha-from-field", () => ({
  ConfigureGeneralCaptchaFormField: () => <div data-testid="general-fields" />,
}));
vi.mock("./configure-block-captcha-form-field", () => ({
  ConfigureBlockCaptchaFormField: () => <div data-testid="block-fields" />,
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const renderModal = (configuration?: unknown) =>
  render(
    <ConfigureCaptchaModal configuration={configuration as never}>
      <DialogTrigger>Open</DialogTrigger>
    </ConfigureCaptchaModal>,
  );

describe("ConfigureCaptchaModal", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
    configsResult = { isLoading: false, isFetching: false, data: { configurations: [] } };
    useProjectStore.setState({ selectedProject: { tenantId: "tg1" } });
  });

  it("opens in add mode and lists the field sections", async () => {
    const user = userEvent.setup();
    renderModal();
    await user.click(screen.getByText("Open"));
    expect(await screen.findByText("Add Captcha Configuration")).toBeInTheDocument();
    expect(screen.getByTestId("general-fields")).toBeInTheDocument();
    expect(screen.getByTestId("block-fields")).toBeInTheDocument();
  });

  it("shows the edit title for an existing configuration", async () => {
    const user = userEvent.setup();
    renderModal({ provider: "recaptcha", isEnable: true });
    await user.click(screen.getByText("Open"));
    expect(await screen.findByText(/Edit Google reCAPTCHA/)).toBeInTheDocument();
  });

  it("saves an existing configuration and shows a success toast", async () => {
    mutateAsync.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    // An existing configuration means the form starts non-dirty; toggling a
    // value would require the (stubbed) fields, so drive submit via the form.
    renderModal({
      provider: "recaptcha",
      isEnable: true,
      captchaKey: "site-key",
      captchaSecret: "secret",
      captchaGenerator: "EasyCaptchaGenerator",
    });
    await user.click(screen.getByText("Open"));
    const form = (await screen.findByText("Captcha Provider")).closest("form")!;
    fireEvent.submit(form);
    await waitFor(() => expect(mutateAsync).toHaveBeenCalled());
    expect(mutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({ projectKey: "tg1", provider: "recaptcha" }),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("shows an error toast when the save is unsuccessful", async () => {
    mutateAsync.mockResolvedValue({ isSuccess: false, errors: ["bad"] });
    const user = userEvent.setup();
    renderModal({
      provider: "recaptcha",
      isEnable: true,
      captchaKey: "site-key",
      captchaSecret: "secret",
      captchaGenerator: "EasyCaptchaGenerator",
    });
    await user.click(screen.getByText("Open"));
    const form = (await screen.findByText("Captcha Provider")).closest("form")!;
    fireEvent.submit(form);
    await waitFor(() => expect(showErrorToast).toHaveBeenCalledWith({ errors: ["bad"] }));
  });
});
