import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { AddEditProviderModal } from "./add-edit-provider-modal";

const savePublicCertificates = vi.fn();
const validateJwksUrl = vi.fn();
const uploadFileMutate = vi.fn();
vi.mock("@blocks-idp/authentication/hooks/use-identifier", () => ({
  useSavePublicCertificates: () => ({ mutateAsync: savePublicCertificates }),
  useValidateJwksUrl: () => ({ mutateAsync: validateJwksUrl }),
}));
vi.mock("@blocks-storage/hooks/use-storage-file", () => ({
  usePublicCertificateFile: () => ({ mutateAsync: uploadFileMutate }),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const openModal = async () => {
  const user = userEvent.setup();
  await user.click(screen.getAllByRole("button", { name: /Add|Edit/ })[0]);
  await screen.findByText("Configure your identity provider");
  return user;
};

describe("AddEditProviderModal", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedProject: { tenantId: "tg1" } });
  });

  it("shows the Add trigger and opens the dialog", async () => {
    render(<AddEditProviderModal />);
    await openModal();
    expect(screen.getByText("Add provider")).toBeInTheDocument();
    expect(screen.getByLabelText("URL")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  });

  it("renders in edit mode from existing data", async () => {
    render(
      <AddEditProviderModal
        existingData={
          {
            jwksUrl: "https://idp.example/jwks",
            issuer: "iss",
            audiences: ["a", "b"],
            providerName: "Keycloak",
          } as never
        }
      />,
    );
    await openModal();
    expect(screen.getByText("Edit provider")).toBeInTheDocument();
    expect(screen.getByLabelText("URL")).toHaveValue("https://idp.example/jwks");
  });

  it("requires a JWKS URL for non-Others providers", async () => {
    render(<AddEditProviderModal />);
    await openModal();
    // Make the form dirty via the issuer field but leave the URL empty.
    fireEvent.change(screen.getByLabelText("Issuer (Optional)"), {
      target: { value: "my-issuer" },
    });
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    fireEvent.click(save);
    expect(await screen.findByText("JWKS URL is required")).toBeInTheDocument();
    expect(savePublicCertificates).not.toHaveBeenCalled();
  });

  it("validates the JWKS URL and saves successfully", async () => {
    validateJwksUrl.mockResolvedValue({ isValid: true });
    savePublicCertificates.mockResolvedValue({ isSuccess: true });
    render(<AddEditProviderModal />);
    await openModal();
    fireEvent.change(screen.getByLabelText("URL"), {
      target: { value: "https://idp.example/jwks" },
    });
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    fireEvent.click(save);
    await waitFor(() => expect(savePublicCertificates).toHaveBeenCalled());
    expect(validateJwksUrl).toHaveBeenCalledWith("https://idp.example/jwks");
    expect(savePublicCertificates).toHaveBeenCalledWith(
      expect.objectContaining({
        projectKey: "tg1",
        jwksUrl: "https://idp.example/jwks",
        providerName: "Keycloak",
      }),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("shows the validation error when the JWKS URL is invalid", async () => {
    validateJwksUrl.mockResolvedValue({ isValid: false, error: "bad url" });
    render(<AddEditProviderModal />);
    await openModal();
    fireEvent.change(screen.getByLabelText("URL"), {
      target: { value: "https://idp.example/jwks" },
    });
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    fireEvent.click(save);
    expect(await screen.findByText("bad url")).toBeInTheDocument();
    expect(savePublicCertificates).not.toHaveBeenCalled();
  });
});
