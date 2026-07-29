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

const makeDirty = () => {
  fireEvent.change(screen.getByLabelText("Issuer (Optional)"), { target: { value: "iss-1" } });
};

const clickSave = async () => {
  const save = screen.getByRole("button", { name: "Save" });
  await waitFor(() => expect(save).toBeEnabled());
  fireEvent.click(save);
};

describe("AddEditProviderModal extra branches", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedProject: { tenantId: "tg1" } as never });
  });

  it("shows an error toast when the save is unsuccessful", async () => {
    validateJwksUrl.mockResolvedValue({ isValid: true });
    savePublicCertificates.mockResolvedValue({ isSuccess: false, errors: "save failed" });
    render(<AddEditProviderModal />);
    await openModal();
    fireEvent.change(screen.getByLabelText("URL"), {
      target: { value: "https://idp.example/jwks" },
    });
    await clickSave();
    await waitFor(() => expect(showErrorToast).toHaveBeenCalledWith({ errors: "save failed" }));
  });

  it("shows an error toast when the save throws", async () => {
    validateJwksUrl.mockResolvedValue({ isValid: true });
    savePublicCertificates.mockRejectedValue(new Error("network"));
    render(<AddEditProviderModal />);
    await openModal();
    fireEvent.change(screen.getByLabelText("URL"), {
      target: { value: "https://idp.example/jwks" },
    });
    await clickSave();
    await waitFor(() => expect(showErrorToast).toHaveBeenCalled());
  });

  it("treats the Others URL as a certificate path when it is not a valid JWKS url", async () => {
    const user = await (async () => {
      render(<AddEditProviderModal />);
      return openModal();
    })();
    await user.click(screen.getByRole("radio", { name: "Others" }));
    // First validation (pre-check) is skipped for Others; the submit-time
    // validation returns invalid so the url is stored as a certificate path.
    validateJwksUrl.mockResolvedValue({ isValid: false });
    savePublicCertificates.mockResolvedValue({ isSuccess: true });
    fireEvent.change(screen.getByLabelText("URL"), {
      target: { value: "https://certs.example/cert.pem" },
    });
    await clickSave();
    await waitFor(() =>
      expect(savePublicCertificates).toHaveBeenCalledWith(
        expect.objectContaining({
          providerName: "Others",
          publicCertificatePath: "https://certs.example/cert.pem",
          jwksUrl: "",
        }),
      ),
    );
  });

  it("stores the Others URL as a jwks url when validation passes", async () => {
    render(<AddEditProviderModal />);
    const user = await openModal();
    await user.click(screen.getByRole("radio", { name: "Others" }));
    validateJwksUrl.mockResolvedValue({ isValid: true });
    savePublicCertificates.mockResolvedValue({ isSuccess: true });
    fireEvent.change(screen.getByLabelText("URL"), {
      target: { value: "https://idp.example/jwks" },
    });
    await clickSave();
    await waitFor(() =>
      expect(savePublicCertificates).toHaveBeenCalledWith(
        expect.objectContaining({ jwksUrl: "https://idp.example/jwks", publicCertificatePath: "" }),
      ),
    );
  });

  it("requires a file when the upload-file method is chosen", async () => {
    render(<AddEditProviderModal />);
    const user = await openModal();
    await user.click(screen.getByRole("radio", { name: "Others" }));
    await user.click(screen.getByRole("radio", { name: "Upload file" }));
    makeDirty();
    await clickSave();
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "Please upload a certificate file" }),
    );
    expect(savePublicCertificates).not.toHaveBeenCalled();
  });

  it("toggles the Others password field visibility", async () => {
    render(<AddEditProviderModal />);
    const user = await openModal();
    await user.click(screen.getByRole("radio", { name: "Others" }));
    const password = screen.getByLabelText("Password (Optional)") as HTMLInputElement;
    expect(password.type).toBe("password");
    // The eye toggle is the button next to the password input.
    const toggle = password.parentElement?.querySelector("button") as HTMLButtonElement;
    await user.click(toggle);
    expect(password.type).toBe("text");
  });

  const chooseUploadFile = async (user: ReturnType<typeof userEvent.setup>) => {
    await user.click(screen.getByRole("radio", { name: "Others" }));
    await user.click(screen.getByRole("radio", { name: "Upload file" }));
  };

  const dropCertificate = (name: string, type = "application/x-x509-ca-cert") => {
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(["cert-bytes"], name, { type });
    fireEvent.change(input, { target: { files: [file] } });
    return file;
  };

  it("uploads a certificate file and saves the returned path", async () => {
    uploadFileMutate.mockResolvedValue({ downloadUrl: "https://cdn.test/cert.crt" });
    savePublicCertificates.mockResolvedValue({ isSuccess: true });
    render(<AddEditProviderModal />);
    const user = await openModal();
    await chooseUploadFile(user);
    dropCertificate("server.crt");
    expect(await screen.findByText("server.crt")).toBeInTheDocument();
    makeDirty();
    await clickSave();
    await waitFor(() => expect(uploadFileMutate).toHaveBeenCalled());
    await waitFor(() =>
      expect(savePublicCertificates).toHaveBeenCalledWith(
        expect.objectContaining({
          publicCertificatePath: "https://cdn.test/cert.crt",
          jwksUrl: "",
        }),
      ),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("rejects a non-certificate file extension", async () => {
    render(<AddEditProviderModal />);
    const user = await openModal();
    await chooseUploadFile(user);
    // Mime matches the dropzone accept list so the file is added, but its
    // extension is not an allowed certificate extension.
    dropCertificate("bad.txt", "application/x-x509-ca-cert");
    await screen.findByText("bad.txt");
    makeDirty();
    await clickSave();
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({
        errors: "Only certificate files are allowed (.crt, .pfx, .der, .p12)",
      }),
    );
    expect(uploadFileMutate).not.toHaveBeenCalled();
  });

  it("shows an error when the upload response has no download url", async () => {
    uploadFileMutate.mockResolvedValue({});
    render(<AddEditProviderModal />);
    const user = await openModal();
    await chooseUploadFile(user);
    dropCertificate("server.crt");
    makeDirty();
    await clickSave();
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "Failed to get upload URL" }),
    );
    expect(savePublicCertificates).not.toHaveBeenCalled();
  });

  it("shows an error when the file upload path save throws", async () => {
    uploadFileMutate.mockRejectedValue(new Error("upload-network"));
    render(<AddEditProviderModal />);
    const user = await openModal();
    await chooseUploadFile(user);
    dropCertificate("server.crt");
    makeDirty();
    await clickSave();
    await waitFor(() => expect(showErrorToast).toHaveBeenCalled());
  });

  it("resets the form state back to public-url when the dialog is closed", async () => {
    render(<AddEditProviderModal />);
    const user = await openModal();
    await chooseUploadFile(user);
    // Cancel closes the dialog, which runs resetForm and returns to public-url.
    await user.click(screen.getAllByRole("button", { name: "Cancel" })[0]);
    await waitFor(() =>
      expect(screen.queryByText("Configure your identity provider")).not.toBeInTheDocument(),
    );
    // Reopen: the method is reset so the URL field (public-url) is shown again.
    await openModal();
    expect(screen.getByLabelText("URL")).toBeInTheDocument();
  });

  it("clears the upload method when switching away from the Others provider", async () => {
    render(<AddEditProviderModal />);
    const user = await openModal();
    await chooseUploadFile(user);
    expect(screen.getByRole("radio", { name: "Upload file" })).toBeInTheDocument();
    // Providers with an icon expose an image alt plus the label, so match loosely.
    await user.click(screen.getByRole("radio", { name: /Keycloak/ }));
    // Upload-file option is only shown for Others; switching back hides it and
    // the public-url URL field is shown again.
    expect(screen.getByLabelText("URL")).toBeInTheDocument();
  });
});
