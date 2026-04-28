import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { LogService } from "./log.service";
import { LOG_ENDPOINTS } from "../constants/endpoint.constant";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";
import {
  mockLogsResponse,
  mockGetLogsPayload,
  mockGetLogsByDatePayload,
} from "../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("LogService", () => {
  let service: LogService;

  beforeEach(() => {
    service = new LogService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getLogs ──────────────────────────────────────────────────────────────
  describe("getLogs", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockLogsResponse);

      const result = await service.getLogs(mockGetLogsPayload);

      expect(http.post).toHaveBeenCalledWith(LOG_ENDPOINTS.GET_LOGS, mockGetLogsPayload);
      expect(result).toEqual(mockLogsResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.getLogs(mockGetLogsPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getLogsByDate ────────────────────────────────────────────────────────
  describe("getLogsByDate", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockLogsResponse);

      const result = await service.getLogsByDate(mockGetLogsByDatePayload);

      expect(http.post).toHaveBeenCalledWith(
        LOG_ENDPOINTS.GET_LOGS_BY_DATE,
        mockGetLogsByDatePayload,
      );
      expect(result).toEqual(mockLogsResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.getLogsByDate(mockGetLogsByDatePayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── getLiveLog ───────────────────────────────────────────────────────────
  describe("getLiveLog", () => {
    it("should GET with correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockLogsResponse);
      const payload = {
        serviceName: "blocks-idp-api",
        lastDate: "2026-01-15T10:30:00.000Z",
        projectKey: TEST_PROJECT_KEY,
      };

      const result = await service.getLiveLog(payload);

      const expectedUrl = `${LOG_ENDPOINTS.LIVE}?Name=${payload.serviceName}&LastDate=${payload.lastDate}&ProjectKey=${payload.projectKey}`;
      expect(http.get).toHaveBeenCalledWith(expectedUrl);
      expect(result).toEqual(mockLogsResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(
        service.getLiveLog({
          serviceName: "blocks-idp-api",
          lastDate: "2026-01-15T10:30:00.000Z",
          projectKey: TEST_PROJECT_KEY,
        }),
      ).rejects.toThrow("Network error");
    });
  });
});
