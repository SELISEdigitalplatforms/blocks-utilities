import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import {
  mockIamServiceFactory,
  mockGetPermissionsPayload,
  mockPermissionsResponse,
  mockGetPermissionByIdPayload,
  mockGetPermissionByIdResponse,
  mockCreatePermissionPayload,
  mockUpdatePermissionPayload,
  mockResourceGroupPayload,
  mockResourceGroupResponse,
} from "../../test-utils/__mocks__";
import { iamService } from "@blocks-idp/iam/services/iam.service";
import {
  useGetPermissions,
  useGetPermissionById,
  useAddPermission,
  useUpdatePermission,
  useGetResourceGroup,
} from "./use-permission";

vi.mock("@blocks-idp/iam/services/iam.service", () => mockIamServiceFactory());

describe("use-permission hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useGetPermissions", () => {
    it("should fetch permissions successfully", async () => {
      vi.mocked(iamService.permission.getPermissions).mockResolvedValue(mockPermissionsResponse);

      const { result } = renderHook(() => useGetPermissions(mockGetPermissionsPayload as never), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockPermissionsResponse);
      expect(iamService.permission.getPermissions).toHaveBeenCalled();
    });
  });

  describe("useGetPermissionById", () => {
    it("should fetch permission by ID successfully", async () => {
      vi.mocked(iamService.permission.getPermissionById).mockResolvedValue(
        mockGetPermissionByIdResponse,
      );

      const { result } = renderHook(() => useGetPermissionById(mockGetPermissionByIdPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockGetPermissionByIdResponse);
      expect(iamService.permission.getPermissionById).toHaveBeenCalledWith(
        mockGetPermissionByIdPayload,
      );
    });
  });

  describe("useAddPermission", () => {
    it("should add permission successfully", async () => {
      vi.mocked(iamService.permission.addPermission).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useAddPermission(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockCreatePermissionPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(iamService.permission.addPermission).toHaveBeenCalledWith(mockCreatePermissionPayload);
    });
  });

  describe("useUpdatePermission", () => {
    it("should update permission successfully", async () => {
      vi.mocked(iamService.permission.updatePermission).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useUpdatePermission(mockGetPermissionByIdPayload), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockUpdatePermissionPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(iamService.permission.updatePermission).toHaveBeenCalledWith(
        mockUpdatePermissionPayload,
      );
    });
  });

  describe("useGetResourceGroup", () => {
    it("should fetch resource groups successfully", async () => {
      vi.mocked(iamService.permission.getResourceGroup).mockResolvedValue(
        mockResourceGroupResponse,
      );

      const { result } = renderHook(() => useGetResourceGroup(mockResourceGroupPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResourceGroupResponse);
      expect(iamService.permission.getResourceGroup).toHaveBeenCalledWith(mockResourceGroupPayload);
    });
  });
});
