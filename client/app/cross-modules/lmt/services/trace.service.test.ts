import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { TraceService } from "./trace.service";
import { TRACE_ENDPOINTS } from "../constants/endpoint.constant";
import {
  mockTrace1,
  mockTrace2,
  mockTracesApiResponse,
  mockTraceByIdApiResponse,
  mockGetTracesPayload,
  mockGetTraceByIdPayload,
} from "../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("TraceService", () => {
  let service: TraceService;

  beforeEach(() => {
    service = new TraceService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getTraces ────────────────────────────────────────────────────────────
  describe("getTraces", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockTracesApiResponse);

      const result = await service.getTraces(mockGetTracesPayload);

      expect(http.post).toHaveBeenCalledWith(TRACE_ENDPOINTS.GET_TRACES, mockGetTracesPayload);
      expect(result.totalCount).toBe(1);
      expect(result.data).toHaveLength(1);
    });

    it("should transform Trace to TraceTree with entryPoint parsing", async () => {
      vi.mocked(http.post).mockResolvedValue(mockTracesApiResponse);

      const result = await service.getTraces(mockGetTracesPayload);
      const tree = result.data[0];

      // "GET /api/users" → method: "GET", actionName: "/api/users"
      expect(tree.entryPoint.method).toBe("GET");
      expect(tree.entryPoint.actionName).toBe("/api/users");
      expect(tree.issues).toEqual([]);
      expect(tree.subEntries).toEqual([]);
      expect(tree.logs).toEqual([]);
    });

    it("should handle operationName with no space (single word)", async () => {
      const singleWordTrace = {
        ...mockTracesApiResponse,
        data: [{ ...mockTrace1, operationName: "HealthCheck" }],
      };
      vi.mocked(http.post).mockResolvedValue(singleWordTrace);

      const result = await service.getTraces(mockGetTracesPayload);
      const tree = result.data[0];

      expect(tree.entryPoint.method).toBe("HealthCheck");
      expect(tree.entryPoint.actionName).toBe("HealthCheck");
    });

    it("should default errors to empty array when null", async () => {
      vi.mocked(http.post).mockResolvedValue({
        data: [mockTrace1],
        errors: null,
        totalCount: 3,
      });

      const result = await service.getTraces(mockGetTracesPayload);

      expect(result.errors).toEqual([]);
      expect(result.totalCount).toBe(3);
    });

    it("should default totalCount to 0 when undefined", async () => {
      vi.mocked(http.post).mockResolvedValue({
        data: [mockTrace1],
        errors: null,
      });

      const result = await service.getTraces(mockGetTracesPayload);

      expect(result.totalCount).toBe(0);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.getTraces(mockGetTracesPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getTraceByTraceId ────────────────────────────────────────────────────
  describe("getTraceByTraceId", () => {
    it("should GET with correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockTraceByIdApiResponse);

      await service.getTraceByTraceId(mockGetTraceByIdPayload);

      const expectedUrl = `${TRACE_ENDPOINTS.GET_TRACE}?TraceId=${mockGetTraceByIdPayload.traceId}&ProjectKey=${mockGetTraceByIdPayload.projectKey}`;
      expect(http.get).toHaveBeenCalledWith(expectedUrl);
    });

    it("should build tree structure from parent-child spans", async () => {
      vi.mocked(http.get).mockResolvedValue(mockTraceByIdApiResponse);

      const result = await service.getTraceByTraceId(mockGetTraceByIdPayload);
      const root = result.data;

      // Root span has no parentId → becomes the root
      expect(root.spanId).toBe(mockTrace1.spanId);
      // Child span has parentSpanId of root → nested in subEntries
      expect(root.subEntries).toHaveLength(1);
      expect(root.subEntries[0].spanId).toBe(mockTrace2.spanId);
    });

    it("should calculate duration from start/end times", async () => {
      vi.mocked(http.get).mockResolvedValue(mockTraceByIdApiResponse);

      const result = await service.getTraceByTraceId(mockGetTraceByIdPayload);
      const root = result.data;

      expect(root.calculatedStartTime).toBeDefined();
      expect(root.calculatedEndTime).toBeDefined();
      expect(typeof root.calculatedDuration).toBe("number");
      expect(root.calculatedDuration).toBeGreaterThanOrEqual(0);
    });

    it("should parse entryPoint from operationName in tree nodes", async () => {
      vi.mocked(http.get).mockResolvedValue(mockTraceByIdApiResponse);

      const result = await service.getTraceByTraceId(mockGetTraceByIdPayload);
      const root = result.data;

      // "GET /api/users" → method: "GET", actionName: "/api/users"
      expect(root.entryPoint.method).toBe("GET");
      expect(root.entryPoint.actionName).toBe("/api/users");

      // "POST /api/auth" → method: "POST", actionName: "/api/auth"
      const child = root.subEntries[0];
      expect(child.entryPoint.method).toBe("POST");
      expect(child.entryPoint.actionName).toBe("/api/auth");
    });

    it("should return empty errors array and totalCount 0", async () => {
      vi.mocked(http.get).mockResolvedValue(mockTraceByIdApiResponse);

      const result = await service.getTraceByTraceId(mockGetTraceByIdPayload);

      expect(result.errors).toEqual([]);
      expect(result.totalCount).toBe(0);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getTraceByTraceId(mockGetTraceByIdPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });
});
