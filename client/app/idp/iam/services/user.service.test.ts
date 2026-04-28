import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { UserService } from "./user.service";
import { UserAccountService } from "./account.service";
import { USER_ENDPOINTS } from "../constants/endpoint.constant";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";
import {
  mockGetUsersPayload,
  mockUsersResponse,
  mockUser,
  mockCreateUserPayload,
  mockUpdateUserPayload,
  mockGetSignUpSettingPayload,
  mockSignUpSettingResponse,
  mockSaveSignUpSettingPayload,
  mockSaveRolesAndPermissionsPayload,
  mockGetSessionsPayload,
  mockGetHistoriesPayload,
  mockGeneratePATPayload,
  mockGetUserRolesPayload,
  mockGetUserPermissionsPayload,
  mockResendActivationPayload,
  mockSuccessResponse,
  MOCK_USER_ITEM_ID,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());
vi.mock("@/lib/utils", () => ({
  parseMongoDBString: vi.fn((str: string) => str),
}));

describe("UserService", () => {
  let service: UserService;

  beforeEach(() => {
    service = new UserService(new UserAccountService());
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getUsers ─────────────────────────────────────────────────────────────
  describe("getUsers", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockUsersResponse);

      const result = await service.getUsers(mockGetUsersPayload);

      expect(http.post).toHaveBeenCalledWith(USER_ENDPOINTS.GET_USERS, mockGetUsersPayload);
      expect(result).toEqual(mockUsersResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.getUsers(mockGetUsersPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getUser ──────────────────────────────────────────────────────────────
  describe("getUser", () => {
    it("should GET from the correct endpoint", async () => {
      const mockResponse = { data: mockUser };
      vi.mocked(http.get).mockResolvedValue(mockResponse);

      const result = await service.getUser();

      expect(http.get).toHaveBeenCalledWith(USER_ENDPOINTS.GET_USER);
      expect(result).toEqual(mockResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getUser()).rejects.toThrow("Network error");
    });
  });

  // ─── getUserById ──────────────────────────────────────────────────────────
  describe("getUserById", () => {
    it("should GET with correct query params", async () => {
      const payload = { id: MOCK_USER_ITEM_ID, projectKey: TEST_PROJECT_KEY };
      vi.mocked(http.get).mockResolvedValue({ data: mockUser });

      const result = await service.getUserById(payload);

      expect(http.get).toHaveBeenCalledWith(
        `${USER_ENDPOINTS.GET_USER}?id=${payload.id}&ProjectKey=${payload.projectKey}`,
      );
      expect(result).toEqual({ data: mockUser });
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(
        service.getUserById({ id: MOCK_USER_ITEM_ID, projectKey: TEST_PROJECT_KEY }),
      ).rejects.toThrow("Network error");
    });
  });

  // ─── addUser ──────────────────────────────────────────────────────────────
  describe("addUser", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.addUser(mockCreateUserPayload);

      expect(http.post).toHaveBeenCalledWith(USER_ENDPOINTS.CREATE, mockCreateUserPayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.addUser(mockCreateUserPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── updateUser ───────────────────────────────────────────────────────────
  describe("updateUser", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.updateUser(mockUpdateUserPayload);

      expect(http.post).toHaveBeenCalledWith(USER_ENDPOINTS.UPDATE, mockUpdateUserPayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.updateUser(mockUpdateUserPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getSignUpSetting ─────────────────────────────────────────────────────
  describe("getSignUpSetting", () => {
    it("should GET with correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockSignUpSettingResponse);

      const result = await service.getSignUpSetting(mockGetSignUpSettingPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${USER_ENDPOINTS.GET_SIGNUP_SETTING}?ProjectKey=${mockGetSignUpSettingPayload.projectKey}`,
      );
      expect(result).toEqual(mockSignUpSettingResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getSignUpSetting(mockGetSignUpSettingPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── saveSignUpSetting ────────────────────────────────────────────────────
  describe("saveSignUpSetting", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.saveSignUpSetting(mockSaveSignUpSettingPayload);

      expect(http.post).toHaveBeenCalledWith(
        USER_ENDPOINTS.SAVE_SIGNUP_SETTING,
        mockSaveSignUpSettingPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.saveSignUpSetting(mockSaveSignUpSettingPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── saveRolesAndPermissions ──────────────────────────────────────────────
  describe("saveRolesAndPermissions", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.saveRolesAndPermissions(mockSaveRolesAndPermissionsPayload);

      expect(http.post).toHaveBeenCalledWith(
        USER_ENDPOINTS.SAVE_ROLES_AND_PERMISSIONS,
        mockSaveRolesAndPermissionsPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(
        service.saveRolesAndPermissions(mockSaveRolesAndPermissionsPayload),
      ).rejects.toThrow("Network error");
    });
  });

  // ─── getSessions ──────────────────────────────────────────────────────────
  describe("getSessions", () => {
    it("should GET with correct query params and parse response", async () => {
      const rawResponse = {
        data: ['{"device":"Chrome"}', '{"device":"Firefox"}'],
        totalCount: 2,
        errors: null,
      };
      vi.mocked(http.get).mockResolvedValue(rawResponse);

      const result = await service.getSessions(mockGetSessionsPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${USER_ENDPOINTS.GET_SESSIONS}?page=${mockGetSessionsPayload.page}&pageSize=${mockGetSessionsPayload.pageSize}&projectkey=${mockGetSessionsPayload.projectKey}&filter.userId=${mockGetSessionsPayload.filter.UserId}`,
      );
      expect(result.totalCount).toBe(2);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getSessions(mockGetSessionsPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getHistories ─────────────────────────────────────────────────────────
  describe("getHistories", () => {
    it("should GET with correct query params and parse response", async () => {
      const rawResponse = {
        data: ['{"action":"login"}'],
        totalCount: 1,
        errors: null,
      };
      vi.mocked(http.get).mockResolvedValue(rawResponse);

      const result = await service.getHistories(mockGetHistoriesPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${USER_ENDPOINTS.GET_HISTORIES}?page=${mockGetHistoriesPayload.page}&pageSize=${mockGetHistoriesPayload.pageSize}&projectkey=${mockGetHistoriesPayload.projectKey}&filter.userId=${mockGetHistoriesPayload.filter.UserId}`,
      );
      expect(result.totalCount).toBe(1);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getHistories(mockGetHistoriesPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getPats ──────────────────────────────────────────────────────────────
  describe("getPats", () => {
    it("should GET from the correct endpoint", async () => {
      const mockResponse = { data: [], errors: null };
      vi.mocked(http.get).mockResolvedValue(mockResponse);

      const result = await service.getPats();

      expect(http.get).toHaveBeenCalledWith(USER_ENDPOINTS.GET_USER_CODES);
      expect(result).toEqual(mockResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getPats()).rejects.toThrow("Network error");
    });
  });

  // ─── generatePats ─────────────────────────────────────────────────────────
  describe("generatePats", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.generatePats(mockGeneratePATPayload);

      expect(http.post).toHaveBeenCalledWith(
        USER_ENDPOINTS.GENERATE_USER_CODE,
        mockGeneratePATPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.generatePats(mockGeneratePATPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getUserRoles ─────────────────────────────────────────────────────────
  describe("getUserRoles", () => {
    it("should GET with correct query params", async () => {
      const mockResponse = { data: ["admin"], errors: null };
      vi.mocked(http.get).mockResolvedValue(mockResponse);

      const result = await service.getUserRoles(mockGetUserRolesPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${USER_ENDPOINTS.GET_USER_ROLES}?Id=${mockGetUserRolesPayload.userId}&ProjectKey=${mockGetUserRolesPayload.projectKey}`,
      );
      expect(result).toEqual(mockResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getUserRoles(mockGetUserRolesPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getUserPermissions ───────────────────────────────────────────────────
  describe("getUserPermissions", () => {
    it("should GET with correct query params", async () => {
      const mockResponse = { data: ["read"], errors: null };
      vi.mocked(http.get).mockResolvedValue(mockResponse);

      const result = await service.getUserPermissions(mockGetUserPermissionsPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${USER_ENDPOINTS.GET_USER_PERMISSIONS}?Id=${mockGetUserPermissionsPayload.userId}&ProjectKey=${mockGetUserPermissionsPayload.projectKey}`,
      );
      expect(result).toEqual(mockResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getUserPermissions(mockGetUserPermissionsPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── accountDeactivate ────────────────────────────────────────────────────
  describe("accountDeactivate", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.accountDeactivate(mockResendActivationPayload);

      expect(http.post).toHaveBeenCalledWith(
        USER_ENDPOINTS.DEACTIVATE,
        mockResendActivationPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.accountDeactivate(mockResendActivationPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });
});
