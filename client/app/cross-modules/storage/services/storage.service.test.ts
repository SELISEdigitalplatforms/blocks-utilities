import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import {
  mockGetDmsFileAndFolderResponse,
  mockUploadDmsFileResponse,
  mockSuccessResponse,
  mockGetDmsPayload,
  mockUploadDmsFilePayload,
  mockCreateDmsFolderPayload,
} from "../test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { StorageService } from "./storage.service";
import { StorageConfiguration } from "./storage-configuration.service";
import { StorageFile } from "./storage-file.service";
import { STORAGE_FILE_ENDPOINTS } from "../constants/endpoint.constant";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("StorageService", () => {
  let service: StorageService;

  beforeEach(() => {
    service = new StorageService(new StorageConfiguration(), new StorageFile());
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── uploadFile ────────────────────────────────────────────────────────────

  describe("uploadFile", () => {
    it("should call http.put with absolute URL, file body, correct headers, and options", async () => {
      vi.mocked(http.put).mockResolvedValue({});

      const file = new File(["content"], "upload.pdf", { type: "application/pdf" });
      const payload = {
        url: "https://s3.amazonaws.com/bucket/upload.pdf?X-Amz-Signature=abc",
        file,
      };

      await service.uploadFile(payload);

      expect(http.put).toHaveBeenCalledWith(
        payload.url,
        payload.file,
        {
          "Content-Type": "application/pdf",
          "x-ms-blob-type": "Blockblob",
        },
        { skipBlocksKey: true, absoluteUrl: true, withCredentials: false },
      );
    });

    it("should use the file MIME type as Content-Type header", async () => {
      vi.mocked(http.put).mockResolvedValue({});

      const pngFile = new File(["data"], "image.png", { type: "image/png" });
      await service.uploadFile({ url: "https://example.com/image.png", file: pngFile });

      expect(http.put).toHaveBeenCalledWith(
        expect.any(String),
        pngFile,
        expect.objectContaining({ "Content-Type": "image/png" }),
        expect.any(Object),
      );
    });

    it("should handle upload errors", async () => {
      vi.mocked(http.put).mockRejectedValue(new Error("Upload failed"));

      const file = new File(["content"], "upload.pdf", { type: "application/pdf" });
      await expect(
        service.uploadFile({ url: "https://example.com/upload.pdf", file }),
      ).rejects.toThrow("Upload failed");
    });
  });

  // ─── uploadFileToLocalStorage ──────────────────────────────────────────────

  describe("uploadFileToLocalStorage", () => {
    it("should call correct endpoint with FormData built from payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const file = new File(["content"], "local-upload.pdf", { type: "application/pdf" });
      const payload = {
        ItemId: "item-1",
        File: file,
        MetaData: "{}",
        Name: "local-upload.pdf",
        ParentDirectoryId: "dir-1",
        Tags: ["tag1"],
        AccessModifier: "public",
        ConfigurationName: "Amazon S3 Config",
        ProjectKey: "project-key",
      };

      await service.uploadFileToLocalStorage(payload);

      expect(http.post).toHaveBeenCalledWith(
        STORAGE_FILE_ENDPOINTS.UPLOAD_TO_LOCAL_STORAGE,
        expect.any(FormData),
      );
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Local storage upload failed"));

      const file = new File(["content"], "file.pdf", { type: "application/pdf" });
      await expect(
        service.uploadFileToLocalStorage({
          ItemId: "item-1",
          File: file,
          MetaData: "{}",
          Name: "file.pdf",
          ParentDirectoryId: "dir-1",
          Tags: [],
          AccessModifier: "public",
          ConfigurationName: "config",
          ProjectKey: "project-key",
        }),
      ).rejects.toThrow("Local storage upload failed");
    });
  });

  // ─── uploadPublicCertificateFile ───────────────────────────────────────────

  describe("uploadPublicCertificateFile", () => {
    it("should call correct endpoint with FormData and TenantId query param", async () => {
      vi.mocked(http.post).mockResolvedValue({ downloadUrl: "https://cert.example.com/cert.pfx" });

      const file = new File(["cert-data"], "client.pfx", { type: "application/x-pkcs12" });
      const payload = { TenantId: "tenant-1", file };

      const result = await service.uploadPublicCertificateFile(payload);

      expect(http.post).toHaveBeenCalledWith(
        `${STORAGE_FILE_ENDPOINTS.UPLOAD_PUBLIC_CERTIFICATE}?TenantId=tenant-1&IsThirdParty=true`,
        expect.any(FormData),
        { Accept: "*/*" },
      );
      expect(result).toEqual({ downloadUrl: "https://cert.example.com/cert.pfx" });
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Certificate upload failed"));

      const file = new File(["cert"], "cert.pfx", { type: "application/x-pkcs12" });
      await expect(
        service.uploadPublicCertificateFile({ TenantId: "tenant-1", file }),
      ).rejects.toThrow("Certificate upload failed");
    });
  });

  // ─── getFilesAndFolders ────────────────────────────────────────────────────

  describe("getFilesAndFolders", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockGetDmsFileAndFolderResponse);

      const result = await service.getFilesAndFolders(mockGetDmsPayload);

      expect(http.post).toHaveBeenCalledWith(
        STORAGE_FILE_ENDPOINTS.GET_DMS_FILE_AND_FOLDER,
        mockGetDmsPayload,
      );
      expect(result).toEqual(mockGetDmsFileAndFolderResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Failed to fetch DMS files"));

      await expect(service.getFilesAndFolders(mockGetDmsPayload)).rejects.toThrow(
        "Failed to fetch DMS files",
      );
    });
  });

  // ─── uploadDmsFile ─────────────────────────────────────────────────────────

  describe("uploadDmsFile", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockUploadDmsFileResponse);

      const result = await service.uploadDmsFile(mockUploadDmsFilePayload);

      expect(http.post).toHaveBeenCalledWith(
        STORAGE_FILE_ENDPOINTS.UPLOAD_DMS_FILE,
        mockUploadDmsFilePayload,
      );
      expect(result).toEqual(mockUploadDmsFileResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("DMS upload failed"));

      await expect(service.uploadDmsFile(mockUploadDmsFilePayload)).rejects.toThrow(
        "DMS upload failed",
      );
    });
  });

  // ─── createDmsFolder ───────────────────────────────────────────────────────

  describe("createDmsFolder", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockUploadDmsFileResponse);

      const result = await service.createDmsFolder(mockCreateDmsFolderPayload);

      expect(http.post).toHaveBeenCalledWith(
        STORAGE_FILE_ENDPOINTS.CREATE_FOLDER,
        mockCreateDmsFolderPayload,
      );
      expect(result).toEqual(mockUploadDmsFileResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Folder creation failed"));

      await expect(service.createDmsFolder(mockCreateDmsFolderPayload)).rejects.toThrow(
        "Folder creation failed",
      );
    });
  });

  // ─── facade delegates ──────────────────────────────────────────────────────

  describe("facade composition", () => {
    it("should expose configuration sub-service", () => {
      expect(service.configuration).toBeInstanceOf(StorageConfiguration);
    });

    it("should expose file sub-service", () => {
      expect(service.file).toBeInstanceOf(StorageFile);
    });
  });
});
