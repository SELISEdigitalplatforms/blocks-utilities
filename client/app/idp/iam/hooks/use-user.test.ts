import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import {
  mockUserServiceFactory,
  mockGetUsersPayload,
  mockUsersResponse,
  mockUser,
  mockCreateUserPayload,
  mockUpdateUserPayload,
  mockGetSignUpSettingPayload,
  mockSignUpSettingResponse,
  mockSaveSignUpSettingPayload,
  mockSaveRolesAndPermissionsPayload,
  mockGetUserRolesPayload,
  MOCK_USER_ITEM_ID,
} from "../../test-utils/__mocks__";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__";
import { userService } from "@blocks-idp/iam/services/user.service";
import {
  useGetUsers,
  useGetUser,
  useGetUserById,
  useAddUser,
  useUpdateUser,
  useGetSignUpSetting,
  useSaveSignUpSetting,
  useAddRolesAndPermissionToUser,
  useGetUserRoles,
  useGetUserPermissions,
} from "./use-user";

vi.mock("@blocks-idp/iam/services/user.service", () => mockUserServiceFactory());

const mockSetUser = vi.fn();
vi.mock("@/store/useAuthStore", () => ({
  useAuthStore: vi.fn(() => ({ setUser: mockSetUser })),
}));

describe("use-user hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useGetUsers", () => {
    it("should fetch users successfully", async () => {
      vi.mocked(userService.getUsers).mockResolvedValue(mockUsersResponse);

      const { result } = renderHook(() => useGetUsers(mockGetUsersPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockUsersResponse);
      expect(userService.getUsers).toHaveBeenCalledWith(mockGetUsersPayload);
    });
  });

  describe("useGetUser", () => {
    it("should fetch current user and set auth store", async () => {
      const mockResponse = { data: mockUser };
      vi.mocked(userService.getUser).mockResolvedValue(mockResponse);

      const { result } = renderHook(() => useGetUser(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(userService.getUser).toHaveBeenCalled();
      expect(mockSetUser).toHaveBeenCalledWith(mockUser);
    });
  });

  describe("useGetUserById", () => {
    it("should fetch user by ID successfully", async () => {
      const mockResponse = { data: mockUser, roles: [], permissions: [], errors: null };
      vi.mocked(userService.getUserById).mockResolvedValue(mockResponse as never);

      const payload = { id: MOCK_USER_ITEM_ID, projectKey: TEST_PROJECT_KEY };
      const { result } = renderHook(() => useGetUserById(payload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResponse);
      expect(userService.getUserById).toHaveBeenCalledWith(payload);
    });
  });

  describe("useAddUser", () => {
    it("should add user successfully", async () => {
      vi.mocked(userService.addUser).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useAddUser(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockCreateUserPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(userService.addUser).toHaveBeenCalledWith(mockCreateUserPayload);
    });
  });

  describe("useUpdateUser", () => {
    it("should update user successfully", async () => {
      vi.mocked(userService.updateUser).mockResolvedValue(undefined as never);

      const { result } = renderHook(
        () => useUpdateUser({ id: MOCK_USER_ITEM_ID, projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      result.current.mutate(mockUpdateUserPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(userService.updateUser).toHaveBeenCalledWith(mockUpdateUserPayload);
    });
  });

  describe("useGetSignUpSetting", () => {
    it("should fetch sign-up setting successfully", async () => {
      vi.mocked(userService.getSignUpSetting).mockResolvedValue(mockSignUpSettingResponse);

      const { result } = renderHook(() => useGetSignUpSetting(mockGetSignUpSettingPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockSignUpSettingResponse);
      expect(userService.getSignUpSetting).toHaveBeenCalledWith(mockGetSignUpSettingPayload);
    });
  });

  describe("useSaveSignUpSetting", () => {
    it("should save sign-up setting successfully", async () => {
      vi.mocked(userService.saveSignUpSetting).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useSaveSignUpSetting(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveSignUpSettingPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(userService.saveSignUpSetting).toHaveBeenCalledWith(mockSaveSignUpSettingPayload);
    });
  });

  describe("useAddRolesAndPermissionToUser", () => {
    it("should save roles and permissions successfully", async () => {
      vi.mocked(userService.saveRolesAndPermissions).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useAddRolesAndPermissionToUser(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveRolesAndPermissionsPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(userService.saveRolesAndPermissions).toHaveBeenCalledWith(
        mockSaveRolesAndPermissionsPayload,
      );
    });
  });

  describe("useGetUserRoles", () => {
    it("should fetch user roles successfully", async () => {
      const mockResponse = { data: [], totalCount: 0, errors: null };
      vi.mocked(userService.getUserRoles).mockResolvedValue(mockResponse as never);

      const { result } = renderHook(() => useGetUserRoles(mockGetUserRolesPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResponse);
      expect(userService.getUserRoles).toHaveBeenCalledWith(mockGetUserRolesPayload);
    });
  });

  describe("useGetUserPermissions", () => {
    it("should fetch user permissions successfully", async () => {
      const mockResponse = { data: [], totalCount: 0, errors: null };
      vi.mocked(userService.getUserPermissions).mockResolvedValue(mockResponse as never);

      const { result } = renderHook(() => useGetUserPermissions(mockGetUserRolesPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResponse);
      expect(userService.getUserPermissions).toHaveBeenCalledWith(mockGetUserRolesPayload);
    });
  });
});
