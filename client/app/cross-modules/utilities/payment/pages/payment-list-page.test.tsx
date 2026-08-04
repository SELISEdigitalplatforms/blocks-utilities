import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type {
  PaymentListData,
  PaymentListItem,
  PaymentQuery,
} from "../models/payment.model";

const refetchMock = vi.fn();
const usePaymentsMock = vi.fn();
let listState: {
  data?: PaymentListData;
  isLoading: boolean;
  isError: boolean;
  isFetching: boolean;
  error?: unknown;
  dataUpdatedAt?: number;
};

vi.mock("../hooks/use-payments", () => ({
  usePayments: (query: PaymentQuery) => {
    usePaymentsMock(query);
    return { ...listState, refetch: refetchMock };
  },
}));

// The refund dialog the list opens talks to react-query on its own.
vi.mock("../hooks/use-create-payment-refund", () => ({
  useCreatePaymentRefund: () => ({
    mutateAsync: vi.fn().mockResolvedValue({ refundId: "r-1", status: "PENDING" }),
    isPending: false,
  }),
}));

import { PaymentListPage } from "./payment-list-page";

/** The amount fields live behind the "More filters" disclosure. */
const expandFilters = () =>
  fireEvent.click(screen.getByRole("button", { name: /More filters/ }));

const payment = (overrides: Partial<PaymentListItem> = {}): PaymentListItem => ({
  paymentDetailId: "payment-1",
  providerName: "ADYEN-ONLINE",
  amount: 10,
  currencyCode: "CHF",
  paymentDateUtc: "2026-07-23T11:00:00Z",
  paymentStatus: "CAPTURED",
  hasPendingRefund: false,
  ...overrides,
});

const listData = (
  items: PaymentListItem[],
  pageInfo: Partial<PaymentListData["pageInfo"]> = {},
): PaymentListData => ({
  items,
  pageInfo: {
    pageSize: 25,
    hasNextPage: false,
    hasPreviousPage: false,
    startCursor: null,
    endCursor: null,
    ...pageInfo,
  },
});

/** The most recent query the page asked the hook for. */
const lastQuery = (): PaymentQuery =>
  usePaymentsMock.mock.calls[usePaymentsMock.mock.calls.length - 1][0];

describe("PaymentListPage", () => {
  beforeEach(() => {
    refetchMock.mockReset();
    usePaymentsMock.mockReset();
    listState = {
      data: listData([payment()]),
      isLoading: false,
      isError: false,
      isFetching: false,
      dataUpdatedAt: 0,
    };
  });

  it("should query the newest payments first by default", () => {
    render(<PaymentListPage />);

    expect(lastQuery()).toEqual(
      expect.objectContaining({
        pageSize: 25,
        sortBy: "paymentDate",
        sortDirection: "desc",
      }),
    );
  });

  it("should report a failed first load", () => {
    listState = {
      isLoading: false,
      isError: true,
      isFetching: false,
      error: new Error("query rejected"),
    };

    render(<PaymentListPage />);

    expect(screen.getByText("query rejected")).toBeTruthy();
  });

  it("should distinguish having no payments from having none that match", () => {
    listState = {
      data: listData([]),
      isLoading: false,
      isError: false,
      isFetching: false,
      dataUpdatedAt: 0,
    };

    render(<PaymentListPage />);

    expect(screen.getByText("No payments yet")).toBeTruthy();
  });

  it("should flip the direction when the active sort column is chosen again", () => {
    render(<PaymentListPage />);

    fireEvent.click(screen.getByRole("button", { name: /Payment date/ }));

    expect(lastQuery()).toEqual(
      expect.objectContaining({ sortBy: "paymentDate", sortDirection: "asc" }),
    );
  });

  it("should sort a newly chosen column by its own natural direction", () => {
    render(<PaymentListPage />);

    fireEvent.click(screen.getByRole("button", { name: /Amount/ }));

    // Amounts read newest-and-largest first, like dates.
    expect(lastQuery()).toEqual(
      expect.objectContaining({ sortBy: "amount", sortDirection: "desc" }),
    );
  });

  it("should page forward from the end cursor", () => {
    listState = {
      data: listData([payment()], { hasNextPage: true, endCursor: "cursor-2" }),
      isLoading: false,
      isError: false,
      isFetching: false,
      dataUpdatedAt: 0,
    };

    render(<PaymentListPage />);
    fireEvent.click(screen.getByRole("button", { name: /Next/ }));

    expect(lastQuery()).toEqual(
      expect.objectContaining({ after: "cursor-2" }),
    );
  });

  it("should page back from the start cursor", () => {
    listState = {
      data: listData([payment()], {
        hasPreviousPage: true,
        startCursor: "cursor-1",
      }),
      isLoading: false,
      isError: false,
      isFetching: false,
      dataUpdatedAt: 0,
    };

    render(<PaymentListPage />);
    fireEvent.click(screen.getByRole("button", { name: /Previous/ }));

    expect(lastQuery()).toEqual(
      expect.objectContaining({ before: "cursor-1" }),
    );
  });

  it("should not offer paging beyond the ends of the result set", () => {
    render(<PaymentListPage />);

    expect(screen.getByRole("button", { name: /Previous/ })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Next/ })).toBeDisabled();
  });

  it("should hold the pager still while a page is being fetched", () => {
    listState = {
      data: listData([payment()], {
        hasNextPage: true,
        hasPreviousPage: true,
        startCursor: "a",
        endCursor: "b",
      }),
      isLoading: false,
      isError: false,
      isFetching: true,
      dataUpdatedAt: 0,
    };

    render(<PaymentListPage />);

    expect(screen.getByRole("button", { name: /Next/ })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Previous/ })).toBeDisabled();
  });

  it("should drop back to the first page when the sort changes", () => {
    listState = {
      data: listData([payment()], { hasNextPage: true, endCursor: "cursor-2" }),
      isLoading: false,
      isError: false,
      isFetching: false,
      dataUpdatedAt: 0,
    };

    render(<PaymentListPage />);
    fireEvent.click(screen.getByRole("button", { name: /Next/ }));
    expect(lastQuery().after).toBe("cursor-2");

    fireEvent.click(screen.getByRole("button", { name: /Amount/ }));

    // A cursor from the previous ordering is meaningless under the new one.
    expect(lastQuery().after).toBeUndefined();
  });

  it("should not apply a filter until it is submitted", () => {
    render(<PaymentListPage />);
    expandFilters();

    fireEvent.change(screen.getByPlaceholderText("0.00"), {
      target: { value: "5" },
    });

    expect(lastQuery().filters.minAmount).toBe("");
  });

  it("should apply the drafted filters on submit", () => {
    render(<PaymentListPage />);
    expandFilters();
    fireEvent.change(screen.getByPlaceholderText("0.00"), {
      target: { value: "5" },
    });

    fireEvent.click(screen.getByRole("button", { name: "Apply filters" }));

    expect(lastQuery().filters.minAmount).toBe("5");
  });

  it("should clear both the draft and the applied filters on reset", () => {
    render(<PaymentListPage />);
    expandFilters();
    fireEvent.change(screen.getByPlaceholderText("0.00"), {
      target: { value: "5" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Apply filters" }));

    fireEvent.click(screen.getByRole("button", { name: /Reset/ }));

    expect(lastQuery().filters.minAmount).toBe("");
    expect(
      (screen.getByPlaceholderText("0.00") as HTMLInputElement).value,
    ).toBe("");
  });

  it("should open the refund dialog for the chosen payment", () => {
    render(<PaymentListPage />);

    fireEvent.click(screen.getAllByRole("button", { name: "Refund" })[0]);

    expect(screen.getByText("Refund payment")).toBeTruthy();
  });
});
