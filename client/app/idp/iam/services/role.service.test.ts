import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { RoleService } from "./role.service";
import { ROLE_ENDPOINTS } from "../constants/endpoint.constant";
import {
  mockGetRolesPayload,
  mockRolesResponse,
  mockGetRolePayload,
  mockGetRoleResponse,
  mockCreateRolePayload,
  mockRole,
  mockUpdateRolePayload,
  mockSetRolesPayload,
  mockSuccessResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("RoleService", () => {
  let service: RoleService;

  beforeEach(() => {
    service = new RoleService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getRoles ─────────────────────────────────────────────────────────────
  describe("getRoles", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockRolesResponse);

      const result = await service.getRoles(mockGetRolesPayload);

      expect(http.post).toHaveBeenCalledWith(ROLE_ENDPOINTS.GET_ROLES, mockGetRolesPayload);
      expect(result).toEqual(mockRolesResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.getRoles(mockGetRolesPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getRoleById ──────────────────────────────────────────────────────────
  describe("getRoleById", () => {
    it("should GET with correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetRoleResponse);

      const result = await service.getRoleById(mockGetRolePayload);

      expect(http.get).toHaveBeenCalledWith(
        `${ROLE_ENDPOINTS.GET_ROLE}?projectKey=${mockGetRolePayload.projectKey}&id=${mockGetRolePayload.id}`,
      );
      expect(result).toEqual(mockGetRoleResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getRoleById(mockGetRolePayload)).rejects.toThrow("Network error");
    });
  });

  // ─── addRole ──────────────────────────────────────────────────────────────
  describe("addRole", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockRole);

      const result = await service.addRole(mockCreateRolePayload);

      expect(http.post).toHaveBeenCalledWith(ROLE_ENDPOINTS.CREATE_ROLE, mockCreateRolePayload);
      expect(result).toEqual(mockRole);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.addRole(mockCreateRolePayload)).rejects.toThrow("Network error");
    });
  });

  // ─── updateRole ───────────────────────────────────────────────────────────
  describe("updateRole", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.updateRole(mockUpdateRolePayload);

      expect(http.post).toHaveBeenCalledWith(ROLE_ENDPOINTS.UPDATE_ROLE, mockUpdateRolePayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.updateRole(mockUpdateRolePayload)).rejects.toThrow("Network error");
    });
  });

  // ─── setRoles ─────────────────────────────────────────────────────────────
  describe("setRoles", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSetRolesPayload);

      const result = await service.setRoles(mockSetRolesPayload);

      expect(http.post).toHaveBeenCalledWith(ROLE_ENDPOINTS.SET_ROLES, { ...mockSetRolesPayload });
      expect(result).toEqual(mockSetRolesPayload);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.setRoles(mockSetRolesPayload)).rejects.toThrow("Network error");
    });
  });
});
