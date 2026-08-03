import { renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { impersonationService } from "@/services/impersonation.service";
import {
  useStartImpersonation,
  useStopImpersonation,
  useImpersonationStatusChecker,
} from "./use-impersonation";

vi.mock("@/services/impersonation.service", () => ({
  impersonationService: {
    startImpersonation: vi.fn(),
    stopImpersonation: vi.fn(),
    impersonationStatus: vi.fn(),
  },
}));

describe("use-impersonation hooks", () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(() => vi.clearAllMocks());

  it("useStartImpersonation calls the service on mutate", async () => {
    vi.mocked(impersonationService.startImpersonation).mockResolvedValue({
      rootTenantId: "r",
    } as never);
    const { result } = renderHook(() => useStartImpersonation(), {
      wrapper: createWrapper(),
    });
    result.current.mutate({ targeted_tenant_id: "t" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(
      vi.mocked(impersonationService.startImpersonation).mock.calls[0][0],
    ).toEqual({ targeted_tenant_id: "t" });
  });

  it("useStopImpersonation calls the service on mutate", async () => {
    vi.mocked(impersonationService.stopImpersonation).mockResolvedValue(
      undefined,
    );
    const { result } = renderHook(() => useStopImpersonation(), {
      wrapper: createWrapper(),
    });
    result.current.mutate(undefined as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(impersonationService.stopImpersonation).toHaveBeenCalled();
  });

  it("useImpersonationStatusChecker queries the status", async () => {
    vi.mocked(impersonationService.impersonationStatus).mockResolvedValue({
      impersonated: false,
    } as never);
    const { result } = renderHook(() => useImpersonationStatusChecker(), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({ impersonated: false });
  });
});
