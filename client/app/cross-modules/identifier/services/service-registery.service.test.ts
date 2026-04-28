import { beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { mockRegisterServiceResponse, mockGetAllServicesResponse } from "../test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { SERVICE_REGISTRY_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";
import { ServiceRegistryService } from "./service-registery.service";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("ServiceRegistryService", () => {
  let service: ServiceRegistryService;

  beforeEach(() => {
    service = new ServiceRegistryService();
  });

  // ─── registerService ────────────────────────────────────────────────────────

  describe("registerService", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockRegisterServiceResponse);

      const payload = {
        serviceName: "Test Service",
        projectKey: "proj-key",
        tags: ["api"],
      };
      const result = await service.registerService(payload);

      expect(http.post).toHaveBeenCalledWith(SERVICE_REGISTRY_ENDPOINTS.REGISTER, payload);
      expect(result).toEqual(mockRegisterServiceResponse);
    });

    it("should pass optional fields correctly", async () => {
      vi.mocked(http.post).mockResolvedValue(mockRegisterServiceResponse);

      const payload = {
        serviceName: "Full Service",
        description: "A test service",
        metadata: '{"version":"1.0.0"}',
        projectKey: "proj-key",
        tags: ["api", "production"],
      };
      await service.registerService(payload);

      expect(http.post).toHaveBeenCalledWith(SERVICE_REGISTRY_ENDPOINTS.REGISTER, payload);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Registration failed"));

      await expect(
        service.registerService({
          serviceName: "Test",
          projectKey: "key",
          tags: [],
        }),
      ).rejects.toThrow("Registration failed");
    });
  });

  // ─── getAllServices ─────────────────────────────────────────────────────────

  describe("getAllServices", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockGetAllServicesResponse);

      const payload = {
        page: 1,
        pageSize: 10,
        projectKey: "proj-key",
      };
      const result = await service.getAllServices(payload);

      expect(http.post).toHaveBeenCalledWith(SERVICE_REGISTRY_ENDPOINTS.GET_ALL, payload);
      expect(result).toEqual(mockGetAllServicesResponse);
    });

    it("should pass sort and filter options", async () => {
      vi.mocked(http.post).mockResolvedValue(mockGetAllServicesResponse);

      const payload = {
        page: 1,
        pageSize: 10,
        projectKey: "proj-key",
        sort: { property: "name", isDescending: false },
        filter: { serviceId: "", serviceName: "api", serviceType: "" },
      };
      await service.getAllServices(payload);

      expect(http.post).toHaveBeenCalledWith(SERVICE_REGISTRY_ENDPOINTS.GET_ALL, payload);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Failed to fetch services"));

      await expect(
        service.getAllServices({ page: 1, pageSize: 10, projectKey: "key" }),
      ).rejects.toThrow("Failed to fetch services");
    });
  });
});
