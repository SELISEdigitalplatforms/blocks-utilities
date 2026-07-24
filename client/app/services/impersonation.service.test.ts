import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { impersonationService } from "./impersonation.service";
import { IMPERSONATE_ENDPOINTS } from "@/idp/authentication/constants";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("impersonationService", () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(() => vi.clearAllMocks());

  it("startImpersonation posts the request", async () => {
    vi.mocked(http.post).mockResolvedValue({ rootTenantId: "r" });
    const request = { targeted_tenant_id: "t" };
    const result = await impersonationService.startImpersonation(request);
    expect(http.post).toHaveBeenCalledWith(
      IMPERSONATE_ENDPOINTS.IMPERSONATE,
      request,
      undefined,
      { absoluteUrl: true },
    );
    expect(result).toEqual({ rootTenantId: "r" });
  });

  it("stopImpersonation posts an empty body", async () => {
    vi.mocked(http.post).mockResolvedValue(undefined);
    await impersonationService.stopImpersonation();
    expect(http.post).toHaveBeenCalledWith(
      IMPERSONATE_ENDPOINTS.STOP_IMPERSONATION,
      {},
      undefined,
      { absoluteUrl: true },
    );
  });

  it("impersonationStatus posts null", async () => {
    vi.mocked(http.post).mockResolvedValue({ impersonated: false });
    const result = await impersonationService.impersonationStatus();
    expect(http.post).toHaveBeenCalledWith(
      IMPERSONATE_ENDPOINTS.IMPERSONATION_STATUS,
      null,
      undefined,
      { absoluteUrl: true },
    );
    expect(result).toEqual({ impersonated: false });
  });
});
