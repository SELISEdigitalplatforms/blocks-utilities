import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const getStoredPaymentMethodsMock = vi.fn();
const removeStoredPaymentMethodMock = vi.fn();
let selectedProject: { tenantId: string } | null;

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject }),
}));

vi.mock("../services/payment.service", () => ({
  paymentService: {
    getStoredPaymentMethods: () => getStoredPaymentMethodsMock(),
    removeStoredPaymentMethod: (id: string) =>
      removeStoredPaymentMethodMock(id),
  },
}));

import {
  useRemoveStoredPaymentMethod,
  useStoredPaymentMethods,
} from "./use-stored-payment-methods";

const wrapper = ({ children }: { children: React.ReactNode }) => {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
};

describe("useStoredPaymentMethods", () => {
  beforeEach(() => {
    getStoredPaymentMethodsMock.mockReset().mockResolvedValue([]);
    removeStoredPaymentMethodMock.mockReset().mockResolvedValue("removed");
    selectedProject = { tenantId: "tenant-1" };
  });

  it("should read the saved methods for the selected project", async () => {
    getStoredPaymentMethodsMock.mockResolvedValue([{ paymentMethodId: "pm-1" }]);

    const { result } = renderHook(() => useStoredPaymentMethods(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([{ paymentMethodId: "pm-1" }]);
  });

  it("should still query when no project is selected", async () => {
    // The tenant only keys the cache; the request itself is scoped by the token.
    selectedProject = null;

    const { result } = renderHook(() => useStoredPaymentMethods(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(getStoredPaymentMethodsMock).toHaveBeenCalled();
  });

  it("should report a failed read", async () => {
    getStoredPaymentMethodsMock.mockRejectedValue(new Error("unavailable"));

    const { result } = renderHook(() => useStoredPaymentMethods(), { wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

describe("useRemoveStoredPaymentMethod", () => {
  beforeEach(() => {
    getStoredPaymentMethodsMock.mockReset().mockResolvedValue([]);
    removeStoredPaymentMethodMock.mockReset().mockResolvedValue("removed");
    selectedProject = { tenantId: "tenant-1" };
  });

  it("should remove the named method and hand back the outcome", async () => {
    const { result } = renderHook(() => useRemoveStoredPaymentMethod(), {
      wrapper,
    });

    const outcome = await result.current.mutateAsync("pm-1");

    expect(removeStoredPaymentMethodMock).toHaveBeenCalledWith("pm-1");
    expect(outcome).toBe("removed");
  });

  it("should surface a removal failure to the caller", async () => {
    removeStoredPaymentMethodMock.mockRejectedValue(new Error("provider said no"));
    const { result } = renderHook(() => useRemoveStoredPaymentMethod(), {
      wrapper,
    });

    await expect(result.current.mutateAsync("pm-1")).rejects.toThrow(
      "provider said no",
    );
  });
});
