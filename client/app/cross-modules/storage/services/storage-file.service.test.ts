import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import {
  mockGetFileByIdResponse,
  mockGetFilesInfoResponse,
  mockPreSignedUrlResponse,
  mockDeleteSuccessResponse,
  mockSuccessResponse,
  mockGetFilePayload,
  mockDeleteFilePayload,
  mockPreSignedUrlPayload,
  mockGetFilesInfoPayload,
} from "../test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { StorageFile } from "./storage-file.service";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";
import { STORAGE_FILE_ENDPOINTS } from "../constants/endpoint.constant";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("StorageFile", () => {
  let service: StorageFile;

  beforeEach(() => {
    service = new StorageFile();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getFileByFileId ───────────────────────────────────────────────────────

  describe("getFileByFileId", () => {
    it("should call correct endpoint with all payload fields", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetFileByIdResponse);

      const result = await service.getFileByFileId(mockGetFilePayload);

      expect(http.get).toHaveBeenCalledWith(
        `${STORAGE_FILE_ENDPOINTS.GET_FILE}?FileId=${mockGetFilePayload.itemId}&ProjectKey=${mockGetFilePayload.projectKey}&ConfigurationName=${mockGetFilePayload.configurationName}`,
      );
      expect(result).toEqual(mockGetFileByIdResponse);
    });

    it("should use empty string when configurationName is undefined", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetFileByIdResponse);

      await service.getFileByFileId({ itemId: "file-1", projectKey: TEST_PROJECT_KEY });

      expect(http.get).toHaveBeenCalledWith(
        `${STORAGE_FILE_ENDPOINTS.GET_FILE}?FileId=file-1&ProjectKey=${TEST_PROJECT_KEY}&ConfigurationName=`,
      );
    });

    it("should handle API errors", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("File not found"));

      await expect(service.getFileByFileId(mockGetFilePayload)).rejects.toThrow("File not found");
    });
  });

  // ─── deleteFileByFileId ────────────────────────────────────────────────────

  describe("deleteFileByFileId", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockDeleteSuccessResponse);

      const result = await service.deleteFileByFileId(mockDeleteFilePayload);

      expect(http.post).toHaveBeenCalledWith(
        STORAGE_FILE_ENDPOINTS.DELETE_FILE,
        mockDeleteFilePayload,
      );
      expect(result).toEqual(mockDeleteSuccessResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Delete failed"));

      await expect(service.deleteFileByFileId(mockDeleteFilePayload)).rejects.toThrow(
        "Delete failed",
      );
    });
  });

  // ─── getPreSignedUrlForUpload ──────────────────────────────────────────────

  describe("getPreSignedUrlForUpload", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockPreSignedUrlResponse);

      const result = await service.getPreSignedUrlForUpload(mockPreSignedUrlPayload);

      expect(http.post).toHaveBeenCalledWith(
        STORAGE_FILE_ENDPOINTS.GET_PRESIGNED_URL,
        mockPreSignedUrlPayload,
      );
      expect(result).toEqual(mockPreSignedUrlResponse);
    });

    it("should return fileId and uploadUrl on success", async () => {
      vi.mocked(http.post).mockResolvedValue(mockPreSignedUrlResponse);

      const result = await service.getPreSignedUrlForUpload(mockPreSignedUrlPayload);

      expect(result.fileId).toBe(mockPreSignedUrlResponse.fileId);
      expect(result.uploadUrl).toBe(mockPreSignedUrlResponse.uploadUrl);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Failed to get pre-signed URL"));

      await expect(service.getPreSignedUrlForUpload(mockPreSignedUrlPayload)).rejects.toThrow(
        "Failed to get pre-signed URL",
      );
    });
  });

  // ─── getFilesInfoUrlForUpload ──────────────────────────────────────────────

  describe("getFilesInfoUrlForUpload", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockGetFilesInfoResponse);

      const result = await service.getFilesInfoUrlForUpload(mockGetFilesInfoPayload);

      expect(http.post).toHaveBeenCalledWith(
        STORAGE_FILE_ENDPOINTS.GET_FILES_INFO,
        mockGetFilesInfoPayload,
      );
      expect(result).toEqual(mockGetFilesInfoResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Failed to fetch files info"));

      await expect(service.getFilesInfoUrlForUpload(mockGetFilesInfoPayload)).rejects.toThrow(
        "Failed to fetch files info",
      );
    });
  });

  // ─── updateFileAdditionalInfo ──────────────────────────────────────────────

  describe("updateFileAdditionalInfo", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = {
        itemId: "file-1",
        additionalProperties: { agentId: "agent-1" },
        projectKey: TEST_PROJECT_KEY,
      };
      const result = await service.updateFileAdditionalInfo(payload);

      expect(http.post).toHaveBeenCalledWith(
        STORAGE_FILE_ENDPOINTS.UPDATE_FILE_ADDITIONAL_INFO,
        payload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Update failed"));

      await expect(
        service.updateFileAdditionalInfo({
          itemId: "file-1",
          additionalProperties: {},
          projectKey: TEST_PROJECT_KEY,
        }),
      ).rejects.toThrow("Update failed");
    });
  });

  // ─── getFilesDownloadUrl ───────────────────────────────────────────────────

  describe("getFilesDownloadUrl", () => {
    it("should call correct endpoint with fileId and projectKey", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetFileByIdResponse);

      const meta = { fileId: "file-1", projectKey: TEST_PROJECT_KEY };
      const result = await service.getFilesDownloadUrl(meta);

      expect(http.get).toHaveBeenCalledWith(
        `${STORAGE_FILE_ENDPOINTS.GET_FILE}?FileId=${meta.fileId}&ProjectKey=${meta.projectKey}`,
      );
      expect(result).toEqual(mockGetFileByIdResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Download URL not found"));

      await expect(
        service.getFilesDownloadUrl({ fileId: "file-1", projectKey: TEST_PROJECT_KEY }),
      ).rejects.toThrow("Download URL not found");
    });
  });
});
