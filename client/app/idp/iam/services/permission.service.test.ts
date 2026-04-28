import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { PermissionService } from "./permission.service";
import { PERMISSION_ENDPOINTS } from "../constants/endpoint.constant";
import {
  mockGetPermissionsPayload,
  mockPermissionsResponse,
  mockGetPermissionByIdPayload,
  mockGetPermissionByIdResponse,
  mockCreatePermissionPayload,
  mockUpdatePermissionPayload,
  mockResourceGroupPayload,
  mockResourceGroupResponse,
  mockSuccessResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("PermissionService", () => {
  let service: PermissionService;

  beforeEach(() => {
    service = new PermissionService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getPermissions ───────────────────────────────────────────────────────
  describe("getPermissions", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockPermissionsResponse);

      const result = await service.getPermissions(mockGetPermissionsPayload);

      expect(http.post).toHaveBeenCalledWith(
        PERMISSION_ENDPOINTS.GET_PERMISSIONS,
        mockGetPermissionsPayload,
      );
      expect(result).toEqual(mockPermissionsResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.getPermissions(mockGetPermissionsPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── getPermissionById ────────────────────────────────────────────────────
  describe("getPermissionById", () => {
    it("should GET with correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetPermissionByIdResponse);

      const result = await service.getPermissionById(mockGetPermissionByIdPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${PERMISSION_ENDPOINTS.GET_PERMISSION}?Id=${mockGetPermissionByIdPayload.id}&ProjectKey=${mockGetPermissionByIdPayload.projectKey}`,
      );
      expect(result).toEqual(mockGetPermissionByIdResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getPermissionById(mockGetPermissionByIdPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── addPermission ────────────────────────────────────────────────────────
  describe("addPermission", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.addPermission(mockCreatePermissionPayload);

      expect(http.post).toHaveBeenCalledWith(
        PERMISSION_ENDPOINTS.CREATE_PERMISSION,
        mockCreatePermissionPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.addPermission(mockCreatePermissionPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── updatePermission ─────────────────────────────────────────────────────
  describe("updatePermission", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.updatePermission(mockUpdatePermissionPayload);

      expect(http.post).toHaveBeenCalledWith(
        PERMISSION_ENDPOINTS.UPDATE_PERMISSION,
        mockUpdatePermissionPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.updatePermission(mockUpdatePermissionPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── getResourceGroup ─────────────────────────────────────────────────────
  describe("getResourceGroup", () => {
    it("should GET with correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockResourceGroupResponse);

      const result = await service.getResourceGroup(mockResourceGroupPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${PERMISSION_ENDPOINTS.GET_RESOURCE_GROUPS}?ProjectKey=${mockResourceGroupPayload.projectKey}`,
      );
      expect(result).toEqual(mockResourceGroupResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getResourceGroup(mockResourceGroupPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });
});
