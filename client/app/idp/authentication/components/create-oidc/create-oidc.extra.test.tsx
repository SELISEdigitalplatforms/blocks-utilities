import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/blocks-kit";

import { CreateOIDC } from "./create-oidc";

const h = vi.hoisted(() => ({
  getPreSign: vi.fn(),
  uploadFile: vi.fn(),
  getFileByFileId: vi.fn(),
}));

vi.mock("@blocks-idp/authentication/hooks/use-auth-oidc", () => ({
  useSaveAuthOidc: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useGetAuthOidcCredential: () => ({ data: undefined, isLoading: false }),
}));
vi.mock("@blocks-storage/hooks/use-storage-file", () => ({
  useGetPreSignedUrlForUpload: () => ({ mutateAsync: h.getPreSign }),
  useUploadFile: () => ({ mutateAsync: h.uploadFile }),
}));
vi.mock("@blocks-storage/services/storage.service", () => ({
  storageService: { file: { getFileByFileId: h.getFileByFileId } },
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const openCreate = async () => {
  const user = userEvent.setup();
  await user.click(screen.getByRole("button", { name: /Create/i }));
  await screen.findByText("New OIDC Client");
  return user;
};

const fileInput = () => document.querySelector('input[type="file"]') as HTMLInputElement;

describe("CreateOIDC image upload", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedProject: { tenantId: "tenant-1" } as never });
  });

  it("rejects an invalid file type", async () => {
    render(<CreateOIDC />);
    await openCreate();
    const bad = new File(["x"], "notes.txt", { type: "text/plain" });
    fireEvent.change(fileInput(), { target: { files: [bad] } });
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith(
        expect.objectContaining({ errors: expect.stringContaining("Invalid file type") }),
      ),
    );
    expect(h.getPreSign).not.toHaveBeenCalled();
  });

  it("rejects a file larger than 5 MB", async () => {
    render(<CreateOIDC />);
    await openCreate();
    const big = new File([new Uint8Array(6 * 1024 * 1024)], "big.png", { type: "image/png" });
    fireEvent.change(fileInput(), { target: { files: [big] } });
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "Image size must be under 5 MB." }),
    );
  });

  it("uploads a valid image and shows a success toast", async () => {
    h.getPreSign.mockResolvedValue({ isSuccess: true, uploadUrl: "https://up.test", fileId: "f1" });
    h.uploadFile.mockResolvedValue(undefined);
    h.getFileByFileId.mockResolvedValue({ url: "https://cdn.test/logo.png" });

    render(<CreateOIDC />);
    await openCreate();
    const good = new File(["img"], "logo.png", { type: "image/png" });
    fireEvent.change(fileInput(), { target: { files: [good] } });

    await waitFor(() =>
      expect(showSuccessToast).toHaveBeenCalledWith({ description: "Logo uploaded successfully" }),
    );
    expect(h.getPreSign).toHaveBeenCalled();
    expect(h.uploadFile).toHaveBeenCalledWith({ url: "https://up.test", file: good });
    // The uploaded logo now renders and a Remove button appears.
    expect(await screen.findByRole("button", { name: "Remove" })).toBeInTheDocument();
  });

  it("shows an error toast when the presigned url request fails", async () => {
    h.getPreSign.mockResolvedValue({ isSuccess: false });
    render(<CreateOIDC />);
    await openCreate();
    const good = new File(["img"], "logo.png", { type: "image/png" });
    fireEvent.change(fileInput(), { target: { files: [good] } });
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({
        errors: "Something went wrong uploading logo",
      }),
    );
  });
});
