import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__";
import { projectService } from "@blocks-identifier/services/project.service";
import {
  useSavePublicCertificates,
  useGetSavedPublicCertificates,
  useValidateJwksUrl,
} from "./use-identifier";

vi.mock("@blocks-identifier/services/project.service", () => ({
  projectService: {
    savePublicCertificate: vi.fn(),
    getPublicCertificateInformation: vi.fn(),
    validateJwksUrl: vi.fn(),
  },
}));

describe("use-identifier hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useSavePublicCertificates", () => {
    it("should save public certificate successfully", async () => {
      const mockPayload: Record<string, string> = {
        projectKey: TEST_PROJECT_KEY,
        url: "https://example.com/jwks",
      };
      vi.mocked(projectService.savePublicCertificate).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useSavePublicCertificates(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockPayload as never);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(projectService.savePublicCertificate).toHaveBeenCalledWith(mockPayload);
    });
  });

  describe("useGetSavedPublicCertificates", () => {
    it("should fetch public certificate information successfully", async () => {
      const mockResponse = { publicCertificateUrl: "https://example.com/cert" };
      vi.mocked(projectService.getPublicCertificateInformation).mockResolvedValue(
        mockResponse as never,
      );

      const { result } = renderHook(() => useGetSavedPublicCertificates(TEST_PROJECT_KEY), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResponse);
      expect(projectService.getPublicCertificateInformation).toHaveBeenCalledWith(TEST_PROJECT_KEY);
    });

    it("should not fetch when projectKey is empty", () => {
      const { result } = renderHook(() => useGetSavedPublicCertificates(""), {
        wrapper: createWrapper(),
      });

      expect(result.current.fetchStatus).toBe("idle");
    });
  });

  describe("useValidateJwksUrl", () => {
    it("should validate JWKS URL successfully", async () => {
      const mockUrl = "https://example.com/.well-known/jwks.json";
      vi.mocked(projectService.validateJwksUrl).mockResolvedValue({ isValid: true });

      const { result } = renderHook(() => useValidateJwksUrl(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockUrl);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(projectService.validateJwksUrl).toHaveBeenCalledWith(mockUrl);
    });
  });
});
