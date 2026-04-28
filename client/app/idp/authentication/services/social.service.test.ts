import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { SSOService } from "./social.service";
import { SSO_ENDPOINTS, AUTH_OIDC_ENDPOINTS } from "../constants/endpoint.constant";
import {
  mockGetSsoCredentialsPayload,
  mockSsoCredentialsResponse,
  mockGetSsoCredentialByIdPayload,
  mockSsoCredential,
  mockSaveSsoPayload,
  mockDeleteSsoPayload,
  mockUpdateSsoStatusPayload,
  mockSuccessResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("SSOService", () => {
  let service: SSOService;

  beforeEach(() => {
    service = new SSOService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getSsoCredentials ────────────────────────────────────────────────────
  describe("getSsoCredentials", () => {
    it("should GET with correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockSsoCredentialsResponse);

      const result = await service.getSsoCredentials(mockGetSsoCredentialsPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${SSO_ENDPOINTS.GET_SSO_CREDENTIALS}?ProjectKey=${mockGetSsoCredentialsPayload.projectKey}`,
      );
      expect(result).toEqual(mockSsoCredentialsResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getSsoCredentials(mockGetSsoCredentialsPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── getSsoCredentialId ───────────────────────────────────────────────────
  describe("getSsoCredentialId", () => {
    it("should GET with itemId and projectKey query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockSsoCredential);

      const result = await service.getSsoCredentialId(mockGetSsoCredentialByIdPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${SSO_ENDPOINTS.GET_SSO_CREDENTIAL}?itemId=${mockGetSsoCredentialByIdPayload.itemId}&projectKey=${mockGetSsoCredentialByIdPayload.projectKey}`,
      );
      expect(result).toEqual(mockSsoCredential);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getSsoCredentialId(mockGetSsoCredentialByIdPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── saveSsoCredential ────────────────────────────────────────────────────
  describe("saveSsoCredential", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.saveSsoCredential(mockSaveSsoPayload);

      expect(http.post).toHaveBeenCalledWith(SSO_ENDPOINTS.SAVE_SSO_CREDENTIAL, mockSaveSsoPayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.saveSsoCredential(mockSaveSsoPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── deleteSsoCredential ──────────────────────────────────────────────────
  describe("deleteSsoCredential", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.deleteSsoCredential(mockDeleteSsoPayload);

      expect(http.post).toHaveBeenCalledWith(
        SSO_ENDPOINTS.DELETE_SSO_CREDENTIAL,
        mockDeleteSsoPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.deleteSsoCredential(mockDeleteSsoPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── updateSsoCredentialStatus ────────────────────────────────────────────
  describe("updateSsoCredentialStatus", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.updateSsoCredentialStatus(mockUpdateSsoStatusPayload);

      expect(http.post).toHaveBeenCalledWith(
        SSO_ENDPOINTS.UPDATE_STATUS,
        mockUpdateSsoStatusPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.updateSsoCredentialStatus(mockUpdateSsoStatusPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── saveBlocksSsoCredential ──────────────────────────────────────────────
  describe("saveBlocksSsoCredential", () => {
    it("should POST to the OIDC save endpoint with payload", async () => {
      const payload = { clientId: "test-client" };
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.saveBlocksSsoCredential(payload);

      expect(http.post).toHaveBeenCalledWith(AUTH_OIDC_ENDPOINTS.SAVE_OIDC_CLIENT, payload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.saveBlocksSsoCredential({})).rejects.toThrow("Network error");
    });
  });

  // ─── getBlocksSsoCredential ───────────────────────────────────────────────
  describe("getBlocksSsoCredential", () => {
    it("should GET with correct query params", async () => {
      const projectKey = "test-project-key";
      vi.mocked(http.get).mockResolvedValue(mockSuccessResponse);

      const result = await service.getBlocksSsoCredential(projectKey);

      expect(http.get).toHaveBeenCalledWith(
        `${AUTH_OIDC_ENDPOINTS.GET_OIDC_CLIENT}?ProjectKey=${projectKey}`,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getBlocksSsoCredential("test-key")).rejects.toThrow("Network error");
    });
  });
});
