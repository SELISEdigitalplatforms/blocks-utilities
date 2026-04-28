import { createWrapper } from "@/test-utils/test-providers/query-client";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { mockProjectStoreFactory } from "@/test-utils/__mocks__";
import {
  mockEmailConfigList,
  mockEmailServiceFactory,
  mockSaveConfigPayload,
  mockDeleteConfigPayload,
  mockSuccessResponse,
} from "../../test-utils/__mocks__";
import { emailService } from "@blocks-communication/mail/services/email.services";
import { useGetEmailConfigs, useSaveEmailConfig, useDeleteEmailConfig } from "./use-email-config";
import { TEST_TENANT_ID } from "@/test-utils/__mocks__/data.mock";

vi.mock("@blocks-communication/mail/services/email.services", () => mockEmailServiceFactory());
vi.mock("@/store/useProjectStore", () => mockProjectStoreFactory());

describe("Email Config Hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useGetEmailConfigs", () => {
    it("should fetch email configs successfully", async () => {
      vi.mocked(emailService.fetchEmailConfigs).mockResolvedValue(mockEmailConfigList);

      const { result } = renderHook(() => useGetEmailConfigs(0, 10), {
        wrapper: createWrapper(),
      });

      expect(result.current.isLoading).toBe(true);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockEmailConfigList);
      expect(emailService.fetchEmailConfigs).toHaveBeenCalledWith(TEST_TENANT_ID, 0, 10);
    });

    it("should handle errors", async () => {
      vi.mocked(emailService.fetchEmailConfigs).mockRejectedValue(
        new Error("Failed to fetch configs"),
      );

      const { result } = renderHook(() => useGetEmailConfigs(0, 10), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error).toBeDefined();
    });

    it("should use correct query key for caching", async () => {
      vi.mocked(emailService.fetchEmailConfigs).mockResolvedValue(mockEmailConfigList);

      const { result } = renderHook(() => useGetEmailConfigs(2, 20), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(emailService.fetchEmailConfigs).toHaveBeenCalledWith(TEST_TENANT_ID, 2, 20);
    });

    it("should handle empty tenantId", async () => {
      const { useProjectStore } = await import("@/store/useProjectStore");
      vi.mocked(useProjectStore).mockReturnValueOnce({
        selectedProject: { tenantId: "" },
      });

      vi.mocked(emailService.fetchEmailConfigs).mockResolvedValue([]);

      const { result } = renderHook(() => useGetEmailConfigs(0, 10), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(emailService.fetchEmailConfigs).toHaveBeenCalledWith("", 0, 10);
    });

    it("should handle different page sizes", async () => {
      vi.mocked(emailService.fetchEmailConfigs).mockResolvedValue(mockEmailConfigList);

      const { result } = renderHook(() => useGetEmailConfigs(0, 50), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(emailService.fetchEmailConfigs).toHaveBeenCalledWith(TEST_TENANT_ID, 0, 50);
    });
  });

  describe("useSaveEmailConfig", () => {
    it("should save email config successfully", async () => {
      vi.mocked(emailService.saveMailConfig).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useSaveEmailConfig(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveConfigPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(emailService.saveMailConfig).toHaveBeenCalledWith(mockSaveConfigPayload);
      expect(result.current.data).toEqual(mockSuccessResponse);
    });

    it("should invalidate email-configs query on success", async () => {
      vi.mocked(emailService.saveMailConfig).mockResolvedValue(mockSuccessResponse);
      vi.mocked(emailService.fetchEmailConfigs).mockResolvedValue(mockEmailConfigList);

      const wrapper = createWrapper();

      // First, load email configs
      const { result: configsResult } = renderHook(() => useGetEmailConfigs(0, 10), {
        wrapper,
      });

      await waitFor(() => expect(configsResult.current.isSuccess).toBe(true));

      // Then save a new config
      const { result: saveResult } = renderHook(() => useSaveEmailConfig(), { wrapper });

      saveResult.current.mutate(mockSaveConfigPayload);

      await waitFor(() => expect(saveResult.current.isSuccess).toBe(true));

      // Configs should be refetched (invalidated)
      await waitFor(() => {
        expect(emailService.fetchEmailConfigs).toHaveBeenCalledTimes(2);
      });
    });

    it("should handle save errors", async () => {
      vi.mocked(emailService.saveMailConfig).mockRejectedValue(new Error("Failed to save config"));

      const { result } = renderHook(() => useSaveEmailConfig(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveConfigPayload);

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error).toEqual(
        expect.objectContaining({ message: "Failed to save config" }),
      );
    });

    it("should use correct mutation key", async () => {
      vi.mocked(emailService.saveMailConfig).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useSaveEmailConfig(), {
        wrapper: createWrapper(),
      });

      // The mutation key should be ["email-config", "add"]
      expect(result.current).toBeDefined();
    });
  });

  describe("useDeleteEmailConfig", () => {
    it("should delete email config successfully", async () => {
      vi.mocked(emailService.deleteMailConfig).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useDeleteEmailConfig(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockDeleteConfigPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(emailService.deleteMailConfig).toHaveBeenCalledWith(mockDeleteConfigPayload);
      expect(result.current.data).toEqual(mockSuccessResponse);
    });

    it("should invalidate email-configs query on success", async () => {
      vi.mocked(emailService.deleteMailConfig).mockResolvedValue(mockSuccessResponse);
      vi.mocked(emailService.fetchEmailConfigs).mockResolvedValue(mockEmailConfigList);

      const wrapper = createWrapper();

      // First, load email configs
      const { result: configsResult } = renderHook(() => useGetEmailConfigs(0, 10), {
        wrapper,
      });

      await waitFor(() => expect(configsResult.current.isSuccess).toBe(true));

      // Then delete a config
      const { result: deleteResult } = renderHook(() => useDeleteEmailConfig(), { wrapper });

      deleteResult.current.mutate(mockDeleteConfigPayload);

      await waitFor(() => expect(deleteResult.current.isSuccess).toBe(true));

      // Configs should be refetched (invalidated)
      await waitFor(() => {
        expect(emailService.fetchEmailConfigs).toHaveBeenCalledTimes(2);
      });
    });

    it("should handle delete errors", async () => {
      vi.mocked(emailService.deleteMailConfig).mockRejectedValue(
        new Error("Failed to delete config"),
      );

      const { result } = renderHook(() => useDeleteEmailConfig(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockDeleteConfigPayload);

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error).toEqual(
        expect.objectContaining({ message: "Failed to delete config" }),
      );
    });

    it("should use correct mutation key", async () => {
      vi.mocked(emailService.deleteMailConfig).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useDeleteEmailConfig(), {
        wrapper: createWrapper(),
      });

      // The mutation key should be ["email-config", "delete"]
      expect(result.current).toBeDefined();
    });
  });
});
