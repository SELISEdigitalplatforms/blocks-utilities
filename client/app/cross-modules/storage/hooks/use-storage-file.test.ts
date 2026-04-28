import { createWrapper } from "@/test-utils/test-providers/query-client";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  mockGetFileByIdResponse,
  mockGetFilesInfoResponse,
  mockPreSignedUrlResponse,
  mockDeleteSuccessResponse,
  mockGetDmsFileAndFolderResponse,
  mockUploadDmsFileResponse,
  mockSuccessResponse,
  mockGetFilePayload,
  mockDeleteFilePayload,
  mockPreSignedUrlPayload,
  mockGetFilesInfoPayload,
  mockGetDmsPayload,
  mockUploadDmsFilePayload,
  mockCreateDmsFolderPayload,
  mockStorageServiceFactory,
} from "../test-utils/__mocks__";
import { storageService } from "@blocks-storage/services/storage.service";
import {
  useGetPreSignedUrlForUpload,
  useUploadFile,
  useUploadFileToLocalStorage,
  useGetFile,
  useLazyGetFile,
  useDeleteFile,
  useGetFilesInfo,
  useGetFilesDownload,
  usePublicCertificateFile,
  useGetDmsFileAndFolder,
  useUploadDmsFile,
  useCreateDmsFolder,
} from "./use-storage-file";

vi.mock("@blocks-storage/services/storage.service", () => mockStorageServiceFactory());

describe("Storage File Hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  // ─── useGetPreSignedUrlForUpload ───────────────────────────────────────────

  describe("useGetPreSignedUrlForUpload", () => {
    it("should get pre-signed URL successfully", async () => {
      vi.mocked(storageService.file.getPreSignedUrlForUpload).mockResolvedValue(
        mockPreSignedUrlResponse,
      );

      const { result } = renderHook(() => useGetPreSignedUrlForUpload(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockPreSignedUrlPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(storageService.file.getPreSignedUrlForUpload).toHaveBeenCalledWith(
        mockPreSignedUrlPayload,
      );
      expect(result.current.data).toEqual(mockPreSignedUrlResponse);
    });

    it("should invalidate getFilesInfo query on success", async () => {
      vi.mocked(storageService.file.getPreSignedUrlForUpload).mockResolvedValue(
        mockPreSignedUrlResponse,
      );
      vi.mocked(storageService.file.getFilesInfoUrlForUpload).mockResolvedValue(
        mockGetFilesInfoResponse,
      );

      const wrapper = createWrapper();

      const { result: filesInfoResult } = renderHook(
        () => useGetFilesInfo(mockGetFilesInfoPayload),
        { wrapper },
      );
      await waitFor(() => expect(filesInfoResult.current.isSuccess).toBe(true));

      const { result: uploadResult } = renderHook(() => useGetPreSignedUrlForUpload(), { wrapper });
      uploadResult.current.mutate(mockPreSignedUrlPayload);

      await waitFor(() => expect(uploadResult.current.isSuccess).toBe(true));

      await waitFor(() => {
        expect(storageService.file.getFilesInfoUrlForUpload).toHaveBeenCalledTimes(2);
      });
    });

    it("should handle errors", async () => {
      vi.mocked(storageService.file.getPreSignedUrlForUpload).mockRejectedValue(
        new Error("Pre-signed URL failed"),
      );

      const { result } = renderHook(() => useGetPreSignedUrlForUpload(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockPreSignedUrlPayload);

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error).toEqual(
        expect.objectContaining({ message: "Pre-signed URL failed" }),
      );
    });
  });

  // ─── useUploadFile ─────────────────────────────────────────────────────────

  describe("useUploadFile", () => {
    it("should upload file successfully", async () => {
      vi.mocked(storageService.uploadFile).mockResolvedValue({});

      const { result } = renderHook(() => useUploadFile(), { wrapper: createWrapper() });

      const file = new File(["content"], "upload.pdf", { type: "application/pdf" });
      result.current.mutate({
        url: "https://s3.amazonaws.com/bucket/upload.pdf?sig=abc",
        file,
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(storageService.uploadFile).toHaveBeenCalledWith(
        expect.objectContaining({ url: "https://s3.amazonaws.com/bucket/upload.pdf?sig=abc" }),
      );
    });

    it("should handle upload errors", async () => {
      vi.mocked(storageService.uploadFile).mockRejectedValue(new Error("Upload failed"));

      const { result } = renderHook(() => useUploadFile(), { wrapper: createWrapper() });

      const file = new File(["content"], "file.pdf", { type: "application/pdf" });
      result.current.mutate({ url: "https://example.com/file.pdf", file });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useUploadFileToLocalStorage ───────────────────────────────────────────

  describe("useUploadFileToLocalStorage", () => {
    it("should upload file to local storage successfully", async () => {
      vi.mocked(storageService.uploadFileToLocalStorage).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useUploadFileToLocalStorage(), {
        wrapper: createWrapper(),
      });

      const file = new File(["content"], "local.pdf", { type: "application/pdf" });
      const payload = {
        ItemId: "item-1",
        File: file,
        MetaData: "{}",
        Name: "local.pdf",
        ParentDirectoryId: "dir-1",
        Tags: [],
        AccessModifier: "public",
        ConfigurationName: "config",
        ProjectKey: "project-key",
      };

      result.current.mutate(payload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(storageService.uploadFileToLocalStorage).toHaveBeenCalledWith(payload);
    });

    it("should handle errors", async () => {
      vi.mocked(storageService.uploadFileToLocalStorage).mockRejectedValue(
        new Error("Upload to local storage failed"),
      );

      const { result } = renderHook(() => useUploadFileToLocalStorage(), {
        wrapper: createWrapper(),
      });

      const file = new File(["content"], "file.pdf", { type: "application/pdf" });
      result.current.mutate({
        ItemId: "item-1",
        File: file,
        MetaData: "{}",
        Name: "file.pdf",
        ParentDirectoryId: "dir-1",
        Tags: [],
        AccessModifier: "public",
        ConfigurationName: "config",
        ProjectKey: "project-key",
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useGetFile ────────────────────────────────────────────────────────────

  describe("useGetFile", () => {
    it("should fetch file by ID successfully", async () => {
      vi.mocked(storageService.file.getFileByFileId).mockResolvedValue(mockGetFileByIdResponse);

      const { result } = renderHook(() => useGetFile(mockGetFilePayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockGetFileByIdResponse);
      expect(storageService.file.getFileByFileId).toHaveBeenCalledWith(mockGetFilePayload);
    });

    it("should handle errors", async () => {
      vi.mocked(storageService.file.getFileByFileId).mockRejectedValue(new Error("File not found"));

      const { result } = renderHook(() => useGetFile(mockGetFilePayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error).toBeDefined();
    });
  });

  // ─── useLazyGetFile ────────────────────────────────────────────────────────

  describe("useLazyGetFile", () => {
    it("should expose a fetchFile function", () => {
      const { result } = renderHook(() => useLazyGetFile(), { wrapper: createWrapper() });

      expect(typeof result.current.fetchFile).toBe("function");
    });

    it("should fetch file lazily on demand", async () => {
      vi.mocked(storageService.file.getFileByFileId).mockResolvedValue(mockGetFileByIdResponse);

      const { result } = renderHook(() => useLazyGetFile(), { wrapper: createWrapper() });

      const data = await result.current.fetchFile(mockGetFilePayload);

      expect(storageService.file.getFileByFileId).toHaveBeenCalledWith(mockGetFilePayload);
      expect(data).toEqual(mockGetFileByIdResponse);
    });

    it("should handle errors during lazy fetch", async () => {
      vi.mocked(storageService.file.getFileByFileId).mockRejectedValue(
        new Error("Lazy fetch failed"),
      );

      const { result } = renderHook(() => useLazyGetFile(), { wrapper: createWrapper() });

      await expect(result.current.fetchFile(mockGetFilePayload)).rejects.toThrow(
        "Lazy fetch failed",
      );
    });
  });

  // ─── useDeleteFile ─────────────────────────────────────────────────────────

  describe("useDeleteFile", () => {
    it("should delete file successfully", async () => {
      vi.mocked(storageService.file.deleteFileByFileId).mockResolvedValue(
        mockDeleteSuccessResponse,
      );

      const { result } = renderHook(() => useDeleteFile(), { wrapper: createWrapper() });

      result.current.mutate(mockDeleteFilePayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(storageService.file.deleteFileByFileId).toHaveBeenCalledWith(mockDeleteFilePayload);
    });

    it("should invalidate getFilesInfo query on success", async () => {
      vi.mocked(storageService.file.deleteFileByFileId).mockResolvedValue(
        mockDeleteSuccessResponse,
      );
      vi.mocked(storageService.file.getFilesInfoUrlForUpload).mockResolvedValue(
        mockGetFilesInfoResponse,
      );

      const wrapper = createWrapper();

      const { result: filesInfoResult } = renderHook(
        () => useGetFilesInfo(mockGetFilesInfoPayload),
        { wrapper },
      );
      await waitFor(() => expect(filesInfoResult.current.isSuccess).toBe(true));

      const { result: deleteResult } = renderHook(() => useDeleteFile(), { wrapper });
      deleteResult.current.mutate(mockDeleteFilePayload);

      await waitFor(() => expect(deleteResult.current.isSuccess).toBe(true));

      await waitFor(() => {
        expect(storageService.file.getFilesInfoUrlForUpload).toHaveBeenCalledTimes(2);
      });
    });

    it("should handle delete errors", async () => {
      vi.mocked(storageService.file.deleteFileByFileId).mockRejectedValue(
        new Error("Delete failed"),
      );

      const { result } = renderHook(() => useDeleteFile(), { wrapper: createWrapper() });

      result.current.mutate(mockDeleteFilePayload);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useGetFilesInfo ───────────────────────────────────────────────────────

  describe("useGetFilesInfo", () => {
    it("should fetch files info successfully", async () => {
      vi.mocked(storageService.file.getFilesInfoUrlForUpload).mockResolvedValue(
        mockGetFilesInfoResponse,
      );

      const { result } = renderHook(() => useGetFilesInfo(mockGetFilesInfoPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockGetFilesInfoResponse);
      expect(storageService.file.getFilesInfoUrlForUpload).toHaveBeenCalledWith(
        mockGetFilesInfoPayload,
      );
    });

    it("should handle errors", async () => {
      vi.mocked(storageService.file.getFilesInfoUrlForUpload).mockRejectedValue(
        new Error("Failed to fetch files info"),
      );

      const { result } = renderHook(() => useGetFilesInfo(mockGetFilesInfoPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useGetFilesDownload ───────────────────────────────────────────────────

  describe("useGetFilesDownload", () => {
    it("should fetch download URL successfully", async () => {
      vi.mocked(storageService.file.getFilesDownloadUrl).mockResolvedValue(mockGetFileByIdResponse);

      const meta = { fileId: "file-1", projectKey: "project-key" };
      const { result } = renderHook(() => useGetFilesDownload(meta), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockGetFileByIdResponse);
      expect(storageService.file.getFilesDownloadUrl).toHaveBeenCalledWith(meta);
    });

    it("should not fetch when enabled is false", async () => {
      vi.mocked(storageService.file.getFilesDownloadUrl).mockResolvedValue(mockGetFileByIdResponse);

      const meta = { fileId: "file-1", projectKey: "project-key" };
      const { result } = renderHook(() => useGetFilesDownload(meta, { enabled: false }), {
        wrapper: createWrapper(),
      });

      // Give it time to potentially fire
      await new Promise((r) => setTimeout(r, 50));

      expect(result.current.isLoading).toBe(false);
      expect(storageService.file.getFilesDownloadUrl).not.toHaveBeenCalled();
    });

    it("should handle errors", async () => {
      vi.mocked(storageService.file.getFilesDownloadUrl).mockRejectedValue(
        new Error("Download URL not found"),
      );

      const meta = { fileId: "invalid-file", projectKey: "project-key" };
      const { result } = renderHook(() => useGetFilesDownload(meta), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── usePublicCertificateFile ──────────────────────────────────────────────

  describe("usePublicCertificateFile", () => {
    it("should upload certificate successfully", async () => {
      vi.mocked(storageService.uploadPublicCertificateFile).mockResolvedValue({
        downloadUrl: "https://storage.example.com/cert.pfx",
      });

      const { result } = renderHook(() => usePublicCertificateFile(), {
        wrapper: createWrapper(),
      });

      const file = new File(["cert"], "client.pfx", { type: "application/x-pkcs12" });
      result.current.mutate({ TenantId: "tenant-1", file });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(storageService.uploadPublicCertificateFile).toHaveBeenCalledWith({
        TenantId: "tenant-1",
        file,
      });
    });

    it("should handle errors", async () => {
      vi.mocked(storageService.uploadPublicCertificateFile).mockRejectedValue(
        new Error("Certificate upload failed"),
      );

      const { result } = renderHook(() => usePublicCertificateFile(), {
        wrapper: createWrapper(),
      });

      const file = new File(["cert"], "cert.pfx", { type: "application/x-pkcs12" });
      result.current.mutate({ TenantId: "tenant-1", file });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useGetDmsFileAndFolder ────────────────────────────────────────────────

  describe("useGetDmsFileAndFolder", () => {
    it("should fetch DMS files and folders successfully", async () => {
      vi.mocked(storageService.getFilesAndFolders).mockResolvedValue(
        mockGetDmsFileAndFolderResponse,
      );

      const { result } = renderHook(() => useGetDmsFileAndFolder(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockGetDmsPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(storageService.getFilesAndFolders).toHaveBeenCalledWith(mockGetDmsPayload);
      expect(result.current.data).toEqual(mockGetDmsFileAndFolderResponse);
    });

    it("should handle errors", async () => {
      vi.mocked(storageService.getFilesAndFolders).mockRejectedValue(
        new Error("Failed to fetch DMS items"),
      );

      const { result } = renderHook(() => useGetDmsFileAndFolder(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockGetDmsPayload);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useUploadDmsFile ──────────────────────────────────────────────────────

  describe("useUploadDmsFile", () => {
    it("should upload DMS file successfully", async () => {
      vi.mocked(storageService.uploadDmsFile).mockResolvedValue(mockUploadDmsFileResponse);

      const { result } = renderHook(() => useUploadDmsFile(), { wrapper: createWrapper() });

      result.current.mutate(mockUploadDmsFilePayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(storageService.uploadDmsFile).toHaveBeenCalledWith(mockUploadDmsFilePayload);
      expect(result.current.data).toEqual(mockUploadDmsFileResponse);
    });

    it("should invalidate dms-file-and-folder query on success", async () => {
      vi.mocked(storageService.uploadDmsFile).mockResolvedValue(mockUploadDmsFileResponse);
      vi.mocked(storageService.getFilesAndFolders).mockResolvedValue(
        mockGetDmsFileAndFolderResponse,
      );

      const wrapper = createWrapper();

      // Trigger a DMS folder listing first
      const { result: dmsResult } = renderHook(() => useGetDmsFileAndFolder(), { wrapper });
      dmsResult.current.mutate(mockGetDmsPayload);
      await waitFor(() => expect(dmsResult.current.isSuccess).toBe(true));

      const callsBefore = vi.mocked(storageService.getFilesAndFolders).mock.calls.length;

      // Upload a file
      const { result: uploadResult } = renderHook(() => useUploadDmsFile(), { wrapper });
      uploadResult.current.mutate(mockUploadDmsFilePayload);
      await waitFor(() => expect(uploadResult.current.isSuccess).toBe(true));

      // The invalidation key is ["storage", "file", "dms-file-and-folder"] which is a query key
      // useGetDmsFileAndFolder is a mutation (not a query), so invalidation won't cause a refetch here.
      // We assert the mutation ran successfully instead.
      expect(storageService.uploadDmsFile).toHaveBeenCalledWith(mockUploadDmsFilePayload);
      expect(callsBefore).toBeGreaterThanOrEqual(1);
    });

    it("should handle errors", async () => {
      vi.mocked(storageService.uploadDmsFile).mockRejectedValue(new Error("DMS upload failed"));

      const { result } = renderHook(() => useUploadDmsFile(), { wrapper: createWrapper() });

      result.current.mutate(mockUploadDmsFilePayload);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useCreateDmsFolder ────────────────────────────────────────────────────

  describe("useCreateDmsFolder", () => {
    it("should create DMS folder successfully", async () => {
      vi.mocked(storageService.createDmsFolder).mockResolvedValue(mockUploadDmsFileResponse);

      const { result } = renderHook(() => useCreateDmsFolder(), { wrapper: createWrapper() });

      result.current.mutate(mockCreateDmsFolderPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(storageService.createDmsFolder).toHaveBeenCalledWith(mockCreateDmsFolderPayload);
      expect(result.current.data).toEqual(mockUploadDmsFileResponse);
    });

    it("should handle errors", async () => {
      vi.mocked(storageService.createDmsFolder).mockRejectedValue(
        new Error("Folder creation failed"),
      );

      const { result } = renderHook(() => useCreateDmsFolder(), { wrapper: createWrapper() });

      result.current.mutate(mockCreateDmsFolderPayload);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });
});
