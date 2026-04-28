import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { mockProjectStoreFactory } from "@/test-utils/__mocks__";
import {
  mockIamConfigurationServiceFactory,
  mockIamConfiguration,
  mockSaveIamConfigPayload,
} from "../../test-utils/__mocks__";
import { configurationService } from "@blocks-idp/iam/services/configuration.service";
import { useGetIamConfiguration, useSaveIamConfiguration } from "./use-iam-configuration";

vi.mock("@blocks-idp/iam/services/configuration.service", () =>
  mockIamConfigurationServiceFactory(),
);
vi.mock("@/store/useProjectStore", () => mockProjectStoreFactory());

describe("use-iam-configuration hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useGetIamConfiguration", () => {
    it("should fetch IAM configuration successfully", async () => {
      const mockResponse = { data: mockIamConfiguration, errors: null };
      vi.mocked(configurationService.getIamConfiguration).mockResolvedValue(mockResponse);

      const { result } = renderHook(() => useGetIamConfiguration(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResponse);
      expect(configurationService.getIamConfiguration).toHaveBeenCalled();
    });
  });

  describe("useSaveIamConfiguration", () => {
    it("should save IAM configuration successfully", async () => {
      vi.mocked(configurationService.saveIamConfiguration).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useSaveIamConfiguration(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveIamConfigPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(configurationService.saveIamConfiguration).toHaveBeenCalledWith(
        mockSaveIamConfigPayload,
      );
    });
  });
});
