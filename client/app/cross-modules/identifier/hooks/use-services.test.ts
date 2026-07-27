import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { serviceRegistryService } from "@blocks-identifier/services/service-registery.service";
import { useRegisterService, useGetAllServices } from "./use-services";

vi.mock("@blocks-identifier/services/service-registery.service", () => ({
  serviceRegistryService: {
    registerService: vi.fn(),
    getAllServices: vi.fn(),
  },
}));

describe("use-services hooks", () => {
  beforeEach(() => vi.clearAllMocks());

  it("useRegisterService invalidates on a successful registration", async () => {
    vi.mocked(serviceRegistryService.registerService).mockResolvedValue({
      isSuccess: true,
    } as never);
    const { result } = renderHook(() => useRegisterService(), {
      wrapper: createWrapper(),
    });
    result.current.mutate({ projectKey: "pk" } as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(serviceRegistryService.registerService).toHaveBeenCalled();
  });

  it("useRegisterService handles a non-success result", async () => {
    vi.mocked(serviceRegistryService.registerService).mockResolvedValue({
      isSuccess: false,
    } as never);
    const { result } = renderHook(() => useRegisterService(), {
      wrapper: createWrapper(),
    });
    result.current.mutate({ projectKey: "pk" } as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("useGetAllServices fetches when a project key is present", async () => {
    vi.mocked(serviceRegistryService.getAllServices).mockResolvedValue({
      data: [],
    } as never);
    const { result } = renderHook(
      () => useGetAllServices({ projectKey: "pk", page: 1, pageSize: 10 } as never),
      { wrapper: createWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(serviceRegistryService.getAllServices).toHaveBeenCalled();
  });

  it("useGetAllServices is disabled without a project key", () => {
    const { result } = renderHook(
      () => useGetAllServices({ projectKey: "", page: 1, pageSize: 10 } as never),
      { wrapper: createWrapper() },
    );
    expect(result.current.fetchStatus).toBe("idle");
  });
});
