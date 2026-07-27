import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { ProfileImageUploader } from "./profile-image-uploader";

const getMe = { data: { data: { profileImageUrl: "https://img/existing.png" } } };
const preSigned = vi.fn();
const uploadFile = vi.fn();
const updateUser = vi.fn();
vi.mock("@/idp/iam/hooks/use-user", () => ({
  useGetMe: () => getMe,
  useUpdateUser: () => ({ mutateAsync: updateUser }),
}));
vi.mock("@blocks-storage/hooks/use-storage-file", () => ({
  useGetPreSignedUrlForUpload: () => ({ mutateAsync: preSigned }),
  useUploadFile: () => ({ mutateAsync: uploadFile }),
}));
const getFileByFileId = vi.fn();
vi.mock("@blocks-storage/services/storage.service", () => ({
  storageService: { file: { getFileByFileId: (...a: unknown[]) => getFileByFileId(...a) } },
}));

const showErrorToast = vi.fn();
const showSuccessToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
}));

const fileInput = (container: HTMLElement) =>
  container.querySelector('input[type="file"]') as HTMLInputElement;

const setFiles = (input: HTMLInputElement, file: File) => {
  Object.defineProperty(input, "files", { value: [file], configurable: true });
  fireEvent.change(input);
};

describe("ProfileImageUploader", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    globalThis.URL.createObjectURL = vi.fn(() => "blob:preview");
  });

  it("renders the current profile image", () => {
    render(<ProfileImageUploader projectKey="pk" id="u1" />);
    expect(screen.getByAltText("Profile Image")).toHaveAttribute(
      "src",
      "https://img/existing.png",
    );
    expect(
      screen.getByRole("button", { name: /Change Image/ }),
    ).toBeInTheDocument();
  });

  it("rejects a file with a disallowed type", () => {
    const { container } = render(
      <ProfileImageUploader projectKey="pk" id="u1" />,
    );
    setFiles(
      fileInput(container),
      new File(["x"], "note.txt", { type: "text/plain" }),
    );
    expect(showErrorToast).toHaveBeenCalledWith({
      errors: "Only image files (PNG, JPG, GIF, WebP, and SVG) are allowed",
    });
    expect(preSigned).not.toHaveBeenCalled();
  });

  it("rejects a file larger than 5MB", () => {
    const { container } = render(
      <ProfileImageUploader projectKey="pk" id="u1" />,
    );
    const big = new File(["x"], "big.png", { type: "image/png" });
    Object.defineProperty(big, "size", { value: 6 * 1024 * 1024 });
    setFiles(fileInput(container), big);
    expect(showErrorToast).toHaveBeenCalledWith({
      errors: "File size must be less than 5MB",
    });
  });

  it("uploads a valid image and shows a success toast", async () => {
    preSigned.mockResolvedValue({
      isSuccess: true,
      fileId: "f1",
      uploadUrl: "https://upload",
    });
    uploadFile.mockResolvedValue({});
    getFileByFileId.mockResolvedValue({ itemId: "f1", url: "https://cdn/f1" });
    updateUser.mockResolvedValue({ isSuccess: true });

    const { container } = render(
      <ProfileImageUploader projectKey="pk" id="u1" />,
    );
    setFiles(
      fileInput(container),
      new File(["x"], "pic.png", { type: "image/png" }),
    );
    await waitFor(() => expect(showSuccessToast).toHaveBeenCalled());
    expect(preSigned).toHaveBeenCalledWith(
      expect.objectContaining({ name: "pic.png", projectKey: "pk" }),
    );
    expect(updateUser).toHaveBeenCalledWith(
      expect.objectContaining({ profileImageId: "f1", profileImageUrl: "https://cdn/f1" }),
    );
  });

  it("shows an error toast when the user update fails", async () => {
    preSigned.mockResolvedValue({
      isSuccess: true,
      fileId: "f1",
      uploadUrl: "https://upload",
    });
    uploadFile.mockResolvedValue({});
    getFileByFileId.mockResolvedValue({ itemId: "f1", url: "https://cdn/f1" });
    updateUser.mockResolvedValue({ isSuccess: false, errors: ["nope"] });

    const { container } = render(
      <ProfileImageUploader projectKey="pk" id="u1" />,
    );
    setFiles(
      fileInput(container),
      new File(["x"], "pic.png", { type: "image/png" }),
    );
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: ["nope"] }),
    );
  });

  it("does nothing when no file is selected", () => {
    const { container } = render(
      <ProfileImageUploader projectKey="pk" id="u1" />,
    );
    const input = fileInput(container);
    Object.defineProperty(input, "files", { value: [], configurable: true });
    fireEvent.change(input);
    expect(preSigned).not.toHaveBeenCalled();
  });
});
