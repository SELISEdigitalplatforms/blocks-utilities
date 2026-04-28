import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { AuthClientsService } from "./auth-clients.service";
import { AUTH_CLIENT_ENDPOINTS } from "../constants/endpoint.constant";
import {
  mockGetClientsPayload,
  mockClientCredentialsResponse,
  mockSaveClientPayload,
  mockDeleteClientPayload,
  mockSuccessResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("AuthClientsService", () => {
  let service: AuthClientsService;

  beforeEach(() => {
    service = new AuthClientsService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getClientCredentials ─────────────────────────────────────────────────
  describe("getClientCredentials", () => {
    it("should GET with correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockClientCredentialsResponse);

      const result = await service.getClientCredentials(mockGetClientsPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${AUTH_CLIENT_ENDPOINTS.GET_CLIENT_CREDENTIALS}?ProjectKey=${mockGetClientsPayload.projectKey}`,
      );
      expect(result).toEqual(mockClientCredentialsResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getClientCredentials(mockGetClientsPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── saveClientCredential ─────────────────────────────────────────────────
  describe("saveClientCredential", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.saveClientCredential(mockSaveClientPayload);

      expect(http.post).toHaveBeenCalledWith(
        AUTH_CLIENT_ENDPOINTS.SAVE_CLIENT_CREDENTIAL,
        mockSaveClientPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.saveClientCredential(mockSaveClientPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── deleteClientCredential ───────────────────────────────────────────────
  describe("deleteClientCredential", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.deleteClientCredential(mockDeleteClientPayload);

      expect(http.post).toHaveBeenCalledWith(
        AUTH_CLIENT_ENDPOINTS.DELETE_CLIENT_CREDENTIAL,
        mockDeleteClientPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.deleteClientCredential(mockDeleteClientPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });
});
