import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { AuthOidc } from "./auth-clients-oidc.service";
import { AUTH_OIDC_ENDPOINTS } from "../constants/endpoint.constant";
import {
  mockGetOidcPayload,
  mockOidcCredentialsResponse,
  mockOidcCredentialResponse,
  mockSaveOidcPayload,
  mockDeleteClientPayload,
  mockSuccessResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("AuthOidc", () => {
  let service: AuthOidc;

  beforeEach(() => {
    service = new AuthOidc();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getOidcCredentials ───────────────────────────────────────────────────
  describe("getOidcCredentials", () => {
    it("should GET with correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockOidcCredentialsResponse);

      const result = await service.getOidcCredentials(mockGetOidcPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${AUTH_OIDC_ENDPOINTS.GET_OIDC_CLIENTS}?ProjectKey=${mockGetOidcPayload.projectKey}`,
      );
      expect(result).toEqual(mockOidcCredentialsResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getOidcCredentials(mockGetOidcPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getOidcCredential ────────────────────────────────────────────────────
  describe("getOidcCredential", () => {
    it("should GET with projectKey and clientId query params", async () => {
      const payload = { ...mockGetOidcPayload, clientId: "test-client-id" };
      vi.mocked(http.get).mockResolvedValue(mockOidcCredentialResponse);

      const result = await service.getOidcCredential(payload);

      expect(http.get).toHaveBeenCalledWith(
        `${AUTH_OIDC_ENDPOINTS.GET_OIDC_CLIENT}?ProjectKey=${payload.projectKey}&ClientId=${payload.clientId}`,
      );
      expect(result).toEqual(mockOidcCredentialResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(
        service.getOidcCredential({ ...mockGetOidcPayload, clientId: "test-client-id" }),
      ).rejects.toThrow("Network error");
    });
  });

  // ─── saveOidcCredential ───────────────────────────────────────────────────
  describe("saveOidcCredential", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.saveOidcCredential(mockSaveOidcPayload);

      expect(http.post).toHaveBeenCalledWith(
        AUTH_OIDC_ENDPOINTS.SAVE_OIDC_CLIENT,
        mockSaveOidcPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.saveOidcCredential(mockSaveOidcPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── deleteOidcCredential ─────────────────────────────────────────────────
  describe("deleteOidcCredential", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.deleteOidcCredential(mockDeleteClientPayload);

      expect(http.post).toHaveBeenCalledWith(
        AUTH_OIDC_ENDPOINTS.DELETE_OIDC_CLIENT,
        mockDeleteClientPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.deleteOidcCredential(mockDeleteClientPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });
});
