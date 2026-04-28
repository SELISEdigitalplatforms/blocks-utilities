import { createWrapper } from "@/test-utils/test-providers/query-client";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { mockProjectStoreFactory } from "@/test-utils/__mocks__";
import {
  mockStorageConfigList,
  mockStorageServiceFactory,
  mockSaveAmazonConfigPayload,
  mockDeleteConfigPayload,
  mockSuccessResponse,
  mockDeleteSuccessResponse,
} from "../test-utils/__mocks__";
import { storageService } from "@blocks-storage/services/storage.service";
import {
  useGetStorageConfigurations,
  useSaveStorageConfiguration,
  useDeleteStorageConfiguration,
} from "./use-storage-configuration";
import { TEST_TENANT_ID } from "@/test-utils/__mocks__/data.mock";

vi.mock("@blocks-storage/services/storage.service", () => mockStorageServiceFactory());
vi.mock("@/store/useProjectStore", () => mockProjectStoreFactory());

describe("Storage Configuration Hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  // ─── useGetStorageConfigurations ───────────────────────────────────────────

  describe("useGetStorageConfigurations", () => {
    it("should fetch storage configurations successfully", async () => {
      vi.mocked(storageService.configuration.gets).mockResolvedValue(mockStorageConfigList);

      const { result } = renderHook(() => useGetStorageConfigurations(), {
        wrapper: createWrapper(),
      });

      expect(result.current.isLoading).toBe(true);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockStorageConfigList);
      expect(storageService.configuration.gets).toHaveBeenCalledWith(TEST_TENANT_ID);
    });

    it("should pass tenantId from project store as projectKey", async () => {
      vi.mocked(storageService.configuration.gets).mockResolvedValue(mockStorageConfigList);

      renderHook(() => useGetStorageConfigurations(), { wrapper: createWrapper() });

      await waitFor(() =>
        expect(storageService.configuration.gets).toHaveBeenCalledWith(TEST_TENANT_ID),
      );
    });

    it("should use empty string when tenantId is not available", async () => {
      const { useProjectStore } = await import("@/store/useProjectStore");
      vi.mocked(useProjectStore).mockReturnValueOnce({
        selectedProject: undefined,
      });

      vi.mocked(storageService.configuration.gets).mockResolvedValue([]);

      const { result } = renderHook(() => useGetStorageConfigurations(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(storageService.configuration.gets).toHaveBeenCalledWith("");
    });

    it("should return empty array when no configs exist", async () => {
      vi.mocked(storageService.configuration.gets).mockResolvedValue([]);

      const { result } = renderHook(() => useGetStorageConfigurations(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual([]);
    });

    it("should handle errors", async () => {
      vi.mocked(storageService.configuration.gets).mockRejectedValue(
        new Error("Failed to fetch configs"),
      );

      const { result } = renderHook(() => useGetStorageConfigurations(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error).toBeDefined();
    });
  });

  // ─── useSaveStorageConfiguration ──────────────────────────────────────────

  describe("useSaveStorageConfiguration", () => {
    it("should save storage configuration successfully", async () => {
      vi.mocked(storageService.configuration.save).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useSaveStorageConfiguration(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveAmazonConfigPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(storageService.configuration.save).toHaveBeenCalledWith(mockSaveAmazonConfigPayload);
      expect(result.current.data).toEqual(mockSuccessResponse);
    });

    it("should invalidate storage configurations query on success", async () => {
      vi.mocked(storageService.configuration.save).mockResolvedValue(mockSuccessResponse);
      vi.mocked(storageService.configuration.gets).mockResolvedValue(mockStorageConfigList);

      const wrapper = createWrapper();

      // Load configurations first
      const { result: configsResult } = renderHook(() => useGetStorageConfigurations(), {
        wrapper,
      });
      await waitFor(() => expect(configsResult.current.isSuccess).toBe(true));

      // Save a new configuration
      const { result: saveResult } = renderHook(() => useSaveStorageConfiguration(), { wrapper });
      saveResult.current.mutate(mockSaveAmazonConfigPayload);

      await waitFor(() => expect(saveResult.current.isSuccess).toBe(true));

      // Configurations should be refetched (invalidated)
      await waitFor(() => {
        expect(storageService.configuration.gets).toHaveBeenCalledTimes(2);
      });
    });

    it("should not invalidate query when isSuccess is false", async () => {
      const failResponse = { errors: ["Validation failed"], isSuccess: false, itemId: "" };
      vi.mocked(storageService.configuration.save).mockResolvedValue(failResponse);
      vi.mocked(storageService.configuration.gets).mockResolvedValue(mockStorageConfigList);

      const wrapper = createWrapper();

      const { result: configsResult } = renderHook(() => useGetStorageConfigurations(), {
        wrapper,
      });
      await waitFor(() => expect(configsResult.current.isSuccess).toBe(true));

      const { result: saveResult } = renderHook(() => useSaveStorageConfiguration(), { wrapper });
      saveResult.current.mutate(mockSaveAmazonConfigPayload);

      await waitFor(() => expect(saveResult.current.isSuccess).toBe(true));

      // gets should only have been called once (no invalidation)
      expect(storageService.configuration.gets).toHaveBeenCalledTimes(1);
    });

    it("should handle save errors", async () => {
      vi.mocked(storageService.configuration.save).mockRejectedValue(
        new Error("Failed to save config"),
      );

      const { result } = renderHook(() => useSaveStorageConfiguration(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveAmazonConfigPayload);

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error).toEqual(
        expect.objectContaining({ message: "Failed to save config" }),
      );
    });
  });

  // ─── useDeleteStorageConfiguration ────────────────────────────────────────

  describe("useDeleteStorageConfiguration", () => {
    it("should delete storage configuration successfully", async () => {
      vi.mocked(storageService.configuration.delete).mockResolvedValue(mockDeleteSuccessResponse);

      const { result } = renderHook(() => useDeleteStorageConfiguration(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockDeleteConfigPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(storageService.configuration.delete).toHaveBeenCalledWith(mockDeleteConfigPayload);
      expect(result.current.data).toEqual(mockDeleteSuccessResponse);
    });

    it("should always invalidate storage configurations query on success", async () => {
      vi.mocked(storageService.configuration.delete).mockResolvedValue(mockDeleteSuccessResponse);
      vi.mocked(storageService.configuration.gets).mockResolvedValue(mockStorageConfigList);

      const wrapper = createWrapper();

      // Load configurations first
      const { result: configsResult } = renderHook(() => useGetStorageConfigurations(), {
        wrapper,
      });
      await waitFor(() => expect(configsResult.current.isSuccess).toBe(true));

      // Delete a configuration
      const { result: deleteResult } = renderHook(() => useDeleteStorageConfiguration(), {
        wrapper,
      });
      deleteResult.current.mutate(mockDeleteConfigPayload);

      await waitFor(() => expect(deleteResult.current.isSuccess).toBe(true));

      // Configurations should be refetched (invalidated)
      await waitFor(() => {
        expect(storageService.configuration.gets).toHaveBeenCalledTimes(2);
      });
    });

    it("should handle delete errors", async () => {
      vi.mocked(storageService.configuration.delete).mockRejectedValue(
        new Error("Failed to delete config"),
      );

      const { result } = renderHook(() => useDeleteStorageConfiguration(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockDeleteConfigPayload);

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error).toEqual(
        expect.objectContaining({ message: "Failed to delete config" }),
      );
    });
  });
});
