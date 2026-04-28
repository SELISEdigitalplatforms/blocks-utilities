import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { jwtClaimServices } from "./auth-jwt-claim.service";
import { PROJECT_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";
import { mockJwtClaimPayload, mockSuccessResponse } from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("JwtClaimServices", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── addJwtClaim ──────────────────────────────────────────────────────────
  describe("addJwtClaim", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await jwtClaimServices.addJwtClaim(mockJwtClaimPayload);

      expect(http.post).toHaveBeenCalledWith(PROJECT_ENDPOINTS.ADD_JWT_CLAIM, mockJwtClaimPayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(jwtClaimServices.addJwtClaim(mockJwtClaimPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });
});
