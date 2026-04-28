import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import {
  mockStorageConfigList,
  mockSuccessResponse,
  mockDeleteSuccessResponse,
  mockSaveAmazonConfigPayload,
  mockSaveAzureConfigPayload,
  mockSaveSftpConfigPayload,
  mockSaveS3CompatibleConfigPayload,
  mockDeleteConfigPayload,
} from "../test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { StorageConfiguration } from "./storage-configuration.service";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";
import { STORAGE_CONFIG_ENDPOINTS } from "../constants/endpoint.constant";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("StorageConfiguration", () => {
  let service: StorageConfiguration;

  beforeEach(() => {
    service = new StorageConfiguration();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── gets ──────────────────────────────────────────────────────────────────

  describe("gets", () => {
    it("should call correct endpoint with projectKey", async () => {
      vi.mocked(http.get).mockResolvedValue(mockStorageConfigList);

      const result = await service.gets(TEST_PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${STORAGE_CONFIG_ENDPOINTS.GET_CONFIGS}?ProjectKey=${TEST_PROJECT_KEY}`,
      );
      expect(result).toEqual(mockStorageConfigList);
    });

    it("should return an empty array when no configs exist", async () => {
      vi.mocked(http.get).mockResolvedValue([]);

      const result = await service.gets(TEST_PROJECT_KEY);

      expect(result).toEqual([]);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.gets(TEST_PROJECT_KEY)).rejects.toThrow("Network error");
    });
  });

  // ─── save ─────────────────────────────────────────────────────────────────

  describe("save", () => {
    it("should call correct endpoint for Amazon strategy and reset SFTP fields", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      await service.save(mockSaveAmazonConfigPayload);

      expect(http.post).toHaveBeenCalledWith(
        STORAGE_CONFIG_ENDPOINTS.SAVE_CONFIG,
        expect.objectContaining({
          storageStrategy: "Amazon",
          accessKey: mockSaveAmazonConfigPayload.accessKey,
          secretKey: mockSaveAmazonConfigPayload.secretKey,
          cloudStorageRegionEndPoint: mockSaveAmazonConfigPayload.cloudStorageRegionEndPoint,
          // SFTP-specific fields from payload override the reset defaults (null wins over "")
          host: null,
          port: null,
          userName: null,
          password: null,
          remoteBasePath: null,
          connectionString: null,
        }),
      );
    });

    it("should call correct endpoint for Azure strategy and reset credential fields", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      await service.save(mockSaveAzureConfigPayload);

      expect(http.post).toHaveBeenCalledWith(
        STORAGE_CONFIG_ENDPOINTS.SAVE_CONFIG,
        expect.objectContaining({
          storageStrategy: "Azure",
          connectionString: mockSaveAzureConfigPayload.connectionString,
          // Credential fields from payload override the reset defaults (null wins over "")
          host: null,
          port: null,
          userName: null,
          password: null,
          accessKey: null,
          secretKey: null,
          cloudStorageRegionEndPoint: null,
        }),
      );
    });

    it("should call correct endpoint for SftpStorage strategy and reset cloud fields", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      await service.save(mockSaveSftpConfigPayload);

      expect(http.post).toHaveBeenCalledWith(
        STORAGE_CONFIG_ENDPOINTS.SAVE_CONFIG,
        expect.objectContaining({
          storageStrategy: "SftpStorage",
          host: mockSaveSftpConfigPayload.host,
          port: mockSaveSftpConfigPayload.port,
          userName: mockSaveSftpConfigPayload.userName,
          password: mockSaveSftpConfigPayload.password,
          remoteBasePath: mockSaveSftpConfigPayload.remoteBasePath,
          // Cloud fields from payload override the reset defaults (null wins over "")
          accessKey: null,
          secretKey: null,
          cloudStorageRegionEndPoint: null,
          connectionString: null,
        }),
      );
    });

    it("should call correct endpoint for S3Compatible strategy and reset incompatible fields", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      await service.save(mockSaveS3CompatibleConfigPayload);

      expect(http.post).toHaveBeenCalledWith(
        STORAGE_CONFIG_ENDPOINTS.SAVE_CONFIG,
        expect.objectContaining({
          storageStrategy: "S3Compatible",
          host: mockSaveS3CompatibleConfigPayload.host,
          accessKey: mockSaveS3CompatibleConfigPayload.accessKey,
          // Incompatible fields from payload override the reset defaults (null wins over "")
          port: null,
          userName: null,
          password: null,
          remoteBasePath: null,
          connectionString: null,
          cloudStorageRegionEndPoint: null,
        }),
      );
    });

    it("should override reset values with provided payload values", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = {
        ...mockSaveAmazonConfigPayload,
        name: "Updated Config",
        updateRequest: true,
        itemId: "config-1",
      };
      await service.save(payload);

      expect(http.post).toHaveBeenCalledWith(
        STORAGE_CONFIG_ENDPOINTS.SAVE_CONFIG,
        expect.objectContaining({
          name: "Updated Config",
          updateRequest: true,
          itemId: "config-1",
        }),
      );
    });

    it("should return the service response", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.save(mockSaveAmazonConfigPayload);

      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Failed to save config"));

      await expect(service.save(mockSaveAmazonConfigPayload)).rejects.toThrow(
        "Failed to save config",
      );
    });
  });

  // ─── delete ───────────────────────────────────────────────────────────────

  describe("delete", () => {
    it("should call correct endpoint with projectKey and configurationName", async () => {
      vi.mocked(http.post).mockResolvedValue(mockDeleteSuccessResponse);

      const result = await service.delete(mockDeleteConfigPayload);

      expect(http.post).toHaveBeenCalledWith(
        `${STORAGE_CONFIG_ENDPOINTS.DELETE_CONFIG}?ProjectKey=${mockDeleteConfigPayload.projectKey}&ConfigurationName=${mockDeleteConfigPayload.configurationName}`,
        {},
      );
      expect(result).toEqual(mockDeleteSuccessResponse);
    });

    it("should send an empty body", async () => {
      vi.mocked(http.post).mockResolvedValue(mockDeleteSuccessResponse);

      await service.delete(mockDeleteConfigPayload);

      expect(http.post).toHaveBeenCalledWith(expect.any(String), {});
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Failed to delete config"));

      await expect(service.delete(mockDeleteConfigPayload)).rejects.toThrow(
        "Failed to delete config",
      );
    });
  });
});
