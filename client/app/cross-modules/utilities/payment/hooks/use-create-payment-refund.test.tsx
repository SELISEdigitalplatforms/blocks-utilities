import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { PaymentListData } from "../models/payment.model";

const createPaymentRefundMock = vi.fn();
let selectedProject: { tenantId: string } | null;

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject }),
}));

vi.mock("../services/payment.service", () => ({
  paymentService: {
    createPaymentRefund: (command: unknown) => createPaymentRefundMock(command),
  },
}));

import { useCreatePaymentRefund } from "./use-create-payment-refund";

const command = {
  paymentDetailId: "pay-1",
  request: { amount: 250, reason: "duplicate charge" },
  idempotencyKey: "idem-1",
};

const listData = (): PaymentListData =>
  ({
    items: [
      { paymentDetailId: "pay-1", hasPendingRefund: false },
      { paymentDetailId: "pay-2", hasPendingRefund: false },
    ],
    pageInfo: { hasNextPage: false },
  }) as unknown as PaymentListData;

let client: QueryClient;

const wrapper = ({ children }: { children: React.ReactNode }) => (
  <QueryClientProvider client={client}>{children}</QueryClientProvider>
);

describe("useCreatePaymentRefund", () => {
  beforeEach(() => {
    createPaymentRefundMock.mockReset().mockResolvedValue({ refundId: "ref-1" });
    selectedProject = { tenantId: "tenant-1" };
    client = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
  });

  it("should send the command through to the service", async () => {
    const { result } = renderHook(() => useCreatePaymentRefund(), { wrapper });

    const outcome = await result.current.mutateAsync(command);

    expect(createPaymentRefundMock).toHaveBeenCalledWith(command);
    expect(outcome).toEqual({ refundId: "ref-1" });
  });

  it("should mark only the refunded payment as pending in the cached list", async () => {
    // The optimistic flag is what stops a second refund being submitted before the
    // invalidated read comes back, so it has to land on the right row and only that row.
    client.setQueryData(["payments", "tenant-1"], listData());
    const { result } = renderHook(() => useCreatePaymentRefund(), { wrapper });

    await result.current.mutateAsync(command);

    await waitFor(() => {
      const cached = client.getQueryData<PaymentListData>(["payments", "tenant-1"]);
      expect(cached?.items[0].hasPendingRefund).toBe(true);
    });
    const cached = client.getQueryData<PaymentListData>(["payments", "tenant-1"]);
    expect(cached?.items[1].hasPendingRefund).toBe(false);
  });

  it("should leave an empty cache slot alone rather than writing a partial list", async () => {
    client.setQueryData(["payments", "tenant-1"], undefined);
    const { result } = renderHook(() => useCreatePaymentRefund(), { wrapper });

    await result.current.mutateAsync(command);

    expect(client.getQueryData(["payments", "tenant-1"])).toBeUndefined();
  });

  it("should key the cache write by the selected project", async () => {
    // A write keyed to the wrong tenant would flag a payment in someone else's list.
    client.setQueryData(["payments", "tenant-1"], listData());
    client.setQueryData(["payments", "tenant-2"], listData());
    const { result } = renderHook(() => useCreatePaymentRefund(), { wrapper });

    await result.current.mutateAsync(command);

    await waitFor(() => {
      const mine = client.getQueryData<PaymentListData>(["payments", "tenant-1"]);
      expect(mine?.items[0].hasPendingRefund).toBe(true);
    });
    const other = client.getQueryData<PaymentListData>(["payments", "tenant-2"]);
    expect(other?.items[0].hasPendingRefund).toBe(false);
  });

  it("should fall back to an empty tenant key when no project is selected", async () => {
    selectedProject = null;
    client.setQueryData(["payments", ""], listData());
    const { result } = renderHook(() => useCreatePaymentRefund(), { wrapper });

    await result.current.mutateAsync(command);

    await waitFor(() => {
      const cached = client.getQueryData<PaymentListData>(["payments", ""]);
      expect(cached?.items[0].hasPendingRefund).toBe(true);
    });
  });

  it("should surface a refused refund and leave the cache untouched", async () => {
    createPaymentRefundMock.mockRejectedValue(new Error("provider declined"));
    client.setQueryData(["payments", "tenant-1"], listData());
    const { result } = renderHook(() => useCreatePaymentRefund(), { wrapper });

    await expect(result.current.mutateAsync(command)).rejects.toThrow("provider declined");

    const cached = client.getQueryData<PaymentListData>(["payments", "tenant-1"]);
    expect(cached?.items[0].hasPendingRefund).toBe(false);
  });
});
