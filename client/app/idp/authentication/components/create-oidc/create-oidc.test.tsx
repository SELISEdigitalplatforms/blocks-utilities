import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { CreateOIDC } from "./create-oidc";

const saveOidc = vi.fn();
let isPending = false;
let existingOidc: { oIDCClientCredential?: Record<string, unknown> } | undefined;
let isLoadingOidc = false;
vi.mock("@blocks-idp/authentication/hooks/use-auth-oidc", () => ({
  useSaveAuthOidc: () => ({ mutateAsync: saveOidc, isPending }),
  useGetAuthOidcCredential: () => ({ data: existingOidc, isLoading: isLoadingOidc }),
}));
vi.mock("@blocks-storage/hooks/use-storage-file", () => ({
  useGetPreSignedUrlForUpload: () => ({ mutateAsync: vi.fn() }),
  useUploadFile: () => ({ mutateAsync: vi.fn() }),
}));
vi.mock("@blocks-storage/services/storage.service", () => ({
  storageService: { file: { getFileByFileId: vi.fn() } },
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const openDialog = async () => {
  const user = userEvent.setup();
  await user.click(screen.getAllByRole("button", { name: /Create/ })[0]);
  await screen.findByText("New OIDC Client");
  return user;
};

const fillValid = () => {
  fireEvent.change(screen.getByPlaceholderText("Enter client name"), {
    target: { value: "My Client" },
  });
  fireEvent.change(screen.getByPlaceholderText("https://example.com/oidc"), {
    target: { value: "https://example.com/oidc" },
  });
  fireEvent.change(screen.getByPlaceholderText("https://example.com"), {
    target: { value: "https://example.com" },
  });
};

describe("CreateOIDC", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
    existingOidc = undefined;
    isLoadingOidc = false;
    useProjectStore.setState({ selectedProject: { tenantId: "tg1" } });
  });

  it("opens the create dialog", async () => {
    render(<CreateOIDC />);
    await openDialog();
    expect(screen.getByText("New OIDC Client")).toBeInTheDocument();
    expect(screen.getByText("Enter details to create a new key")).toBeInTheDocument();
  });

  it("creates an OIDC client and reports success", async () => {
    saveOidc.mockResolvedValue({ isSuccess: true });
    render(<CreateOIDC />);
    await openDialog();
    fillValid();
    const add = screen.getByRole("button", { name: "Add" });
    await waitFor(() => expect(add).toBeEnabled());
    fireEvent.click(add);
    await waitFor(() => expect(saveOidc).toHaveBeenCalled());
    expect(saveOidc).toHaveBeenCalledWith(
      expect.objectContaining({
        projectKey: "tg1",
        redirectUri: "https://example.com/oidc",
        audience: "https://example.com",
        clientDisplayName: "My Client",
      }),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("shows an error toast when the save is unsuccessful", async () => {
    saveOidc.mockResolvedValue({ isSuccess: false, error: "boom" });
    render(<CreateOIDC />);
    await openDialog();
    fillValid();
    const add = screen.getByRole("button", { name: "Add" });
    await waitFor(() => expect(add).toBeEnabled());
    fireEvent.click(add);
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "boom" }),
    );
  });

  it("renders in edit mode and prefills from the existing credential", async () => {
    existingOidc = {
      oIDCClientCredential: {
        redirectUri: "https://edit.example/oidc",
        audience: "https://edit.example",
        scope: "openid",
        clientDisplayName: "Existing",
        clientBrandColor: "#123456",
      },
    };
    const user = userEvent.setup();
    render(<CreateOIDC itemId="oidc-1" />);
    await user.click(screen.getAllByRole("button")[0]);
    await screen.findByText("Edit OIDC Client");
    expect(screen.getByText("Edit OIDC Client")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter client name")).toHaveValue("Existing");
    expect(screen.getByRole("button", { name: "Update" })).toBeInTheDocument();
  });
});
