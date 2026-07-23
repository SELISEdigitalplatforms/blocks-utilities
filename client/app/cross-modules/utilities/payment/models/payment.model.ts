export const PAYMENT_SORT_FIELDS = [
  "providerName",
  "amount",
  "paymentDate",
  "paymentStatus",
] as const;

export const PAYMENT_SORT_DIRECTIONS = ["asc", "desc"] as const;

export type PaymentSortField = (typeof PAYMENT_SORT_FIELDS)[number];
export type PaymentSortDirection = (typeof PAYMENT_SORT_DIRECTIONS)[number];

export interface PaymentListItem {
  paymentDetailId: string;
  providerName: string;
  amount: number;
  currencyCode: string;
  paymentDateUtc: string;
  paymentStatus: string;
  hasPendingRefund: boolean;
}

export interface PaymentPageInfo {
  pageSize: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
  startCursor: string | null;
  endCursor: string | null;
}

export interface PaymentListData {
  items: PaymentListItem[];
  pageInfo: PaymentPageInfo;
}

export interface PaymentApiError {
  code: string;
  message: string;
  fields?: Record<string, string[]> | null;
  traceId?: string;
}

export interface PaymentApiResponse<T> {
  success: boolean;
  data: T | null;
  error: PaymentApiError | null;
  meta: {
    correlationId: string;
    timestampUtc: string;
  };
}

export interface PaymentFilters {
  providerNames: string[];
  paymentStatuses: string[];
  minAmount: string;
  maxAmount: string;
  paymentDateFrom: string;
  paymentDateTo: string;
  currencyCode: string;
  orderId: string;
  paymentDetailId: string;
  paymentFlow: string;
}

export interface PaymentQuery {
  pageSize: number;
  filters: PaymentFilters;
  sortBy: PaymentSortField;
  sortDirection: PaymentSortDirection;
  after?: string;
  before?: string;
}

export const EMPTY_PAYMENT_FILTERS: PaymentFilters = {
  providerNames: [],
  paymentStatuses: [],
  minAmount: "",
  maxAmount: "",
  paymentDateFrom: "",
  paymentDateTo: "",
  currencyCode: "",
  orderId: "",
  paymentDetailId: "",
  paymentFlow: "",
};
