import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import {
  mockRoleServiceFactory,
  mockGetRolesPayload,
  mockRolesResponse,
  mockGetRolePayload,
  mockGetRoleResponse,
  mockCreateRolePayload,
  mockUpdateRolePayload,
  mockSetRolesPayload,
} from "../../test-utils/__mocks__";
import { roleService } from "@blocks-idp/iam/services/role.service";
import { useGetRoles, useGetRoleById, useAddRole, useUpdateRole, useSetRoles } from "./use-roles";

vi.mock("@blocks-idp/iam/services/role.service", () => mockRoleServiceFactory());

describe("use-roles hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useGetRoles", () => {
    it("should fetch roles successfully", async () => {
      vi.mocked(roleService.getRoles).mockResolvedValue(mockRolesResponse);

      const { result } = renderHook(() => useGetRoles(mockGetRolesPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockRolesResponse);
      expect(roleService.getRoles).toHaveBeenCalledWith(mockGetRolesPayload);
    });
  });

  describe("useGetRoleById", () => {
    it("should fetch a role by ID successfully", async () => {
      vi.mocked(roleService.getRoleById).mockResolvedValue(mockGetRoleResponse);

      const { result } = renderHook(() => useGetRoleById(mockGetRolePayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockGetRoleResponse);
      expect(roleService.getRoleById).toHaveBeenCalledWith(mockGetRolePayload);
    });
  });

  describe("useAddRole", () => {
    it("should add a role successfully", async () => {
      vi.mocked(roleService.addRole).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useAddRole(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockCreateRolePayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(roleService.addRole).toHaveBeenCalledWith(mockCreateRolePayload);
    });
  });

  describe("useUpdateRole", () => {
    it("should update a role successfully", async () => {
      vi.mocked(roleService.updateRole).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useUpdateRole(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockUpdateRolePayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(roleService.updateRole).toHaveBeenCalledWith(mockUpdateRolePayload);
    });
  });

  describe("useSetRoles", () => {
    it("should set roles on permission successfully", async () => {
      vi.mocked(roleService.setRoles).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useSetRoles("admin"), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSetRolesPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(roleService.setRoles).toHaveBeenCalledWith(mockSetRolesPayload);
    });
  });
});
