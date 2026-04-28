import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import {
  mockIamServiceFactory,
  mockGetOrganizationsPayload,
  mockOrganizationsResponse,
  mockGetOrganizationByIdPayload,
  mockGetOrganizationByIdResponse,
  mockSaveOrganizationPayload,
  mockOrganizationConfigResponse,
  mockSaveOrganizationConfigPayload,
} from "../../test-utils/__mocks__";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__";
import { iamService } from "@blocks-idp/iam/services/iam.service";
import {
  useGetOrganizations,
  useGetOrganizationById,
  useSaveOrganization,
  useGetOrganizationConfig,
  useSaveOrganizationConfig,
} from "./use-organization";

vi.mock("@blocks-idp/iam/services/iam.service", () => mockIamServiceFactory());

describe("use-organization hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useGetOrganizations", () => {
    it("should fetch organizations successfully", async () => {
      vi.mocked(iamService.organization.getOrganizations).mockResolvedValue(
        mockOrganizationsResponse,
      );

      const { result } = renderHook(() => useGetOrganizations(mockGetOrganizationsPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockOrganizationsResponse);
      expect(iamService.organization.getOrganizations).toHaveBeenCalledWith({
        page: mockGetOrganizationsPayload.page,
        pageSize: mockGetOrganizationsPayload.pageSize,
        projectKey: mockGetOrganizationsPayload.projectKey,
      });
    });
  });

  describe("useGetOrganizationById", () => {
    it("should fetch organization by ID successfully", async () => {
      vi.mocked(iamService.organization.getOrganizationById).mockResolvedValue(
        mockGetOrganizationByIdResponse,
      );

      const { result } = renderHook(() => useGetOrganizationById(mockGetOrganizationByIdPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockGetOrganizationByIdResponse);
      expect(iamService.organization.getOrganizationById).toHaveBeenCalledWith(
        mockGetOrganizationByIdPayload,
      );
    });

    it("should not fetch when itemId is empty", () => {
      const { result } = renderHook(
        () => useGetOrganizationById({ itemId: "", projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe("idle");
    });
  });

  describe("useSaveOrganization", () => {
    it("should save organization successfully", async () => {
      vi.mocked(iamService.organization.saveOrganization).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useSaveOrganization(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveOrganizationPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(iamService.organization.saveOrganization).toHaveBeenCalledWith(
        mockSaveOrganizationPayload,
      );
    });
  });

  describe("useGetOrganizationConfig", () => {
    it("should fetch organization config successfully", async () => {
      vi.mocked(iamService.organization.getOrganizationConfig).mockResolvedValue(
        mockOrganizationConfigResponse,
      );

      const { result } = renderHook(() => useGetOrganizationConfig(TEST_PROJECT_KEY), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockOrganizationConfigResponse);
      expect(iamService.organization.getOrganizationConfig).toHaveBeenCalledWith(TEST_PROJECT_KEY);
    });

    it("should not fetch when projectKey is empty", () => {
      const { result } = renderHook(() => useGetOrganizationConfig(""), {
        wrapper: createWrapper(),
      });

      expect(result.current.fetchStatus).toBe("idle");
    });
  });

  describe("useSaveOrganizationConfig", () => {
    it("should save organization config successfully", async () => {
      vi.mocked(iamService.organization.saveOrganizationConfig).mockResolvedValue(
        undefined as never,
      );

      const { result } = renderHook(() => useSaveOrganizationConfig(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveOrganizationConfigPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(iamService.organization.saveOrganizationConfig).toHaveBeenCalledWith(
        mockSaveOrganizationConfigPayload,
      );
    });
  });
});
