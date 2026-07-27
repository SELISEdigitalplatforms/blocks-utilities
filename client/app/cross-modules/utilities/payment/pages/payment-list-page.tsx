import { useMemo, useState } from "react";
import {
  AlertCircle,
  ChevronLeft,
  ChevronRight,
  CreditCard,
  RefreshCw,
  SearchX,
} from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { PAYMENT_PAGE_SIZE_OPTIONS } from "../constants/payment.constants";
import { usePayments } from "../hooks/use-payments";
import { EMPTY_PAYMENT_FILTERS } from "../models/payment.model";
import type {
  PaymentFilters,
  PaymentListItem,
  PaymentQuery,
  PaymentSortDirection,
  PaymentSortField,
} from "../models/payment.model";
import { PaymentFiltersPanel } from "../components/payment-filters";
import { PaymentListSkeleton } from "../components/payment-list-skeleton";
import { PaymentRefundDialog } from "../components/payment-refund-dialog";
import { PaymentTable } from "../components/payment-table";

const copyEmptyFilters = (): PaymentFilters => ({
  ...EMPTY_PAYMENT_FILTERS,
  providerNames: [],
  paymentStatuses: [],
});

const countActiveFilters = (filters: PaymentFilters) =>
  Object.values(filters).filter((value) =>
    Array.isArray(value) ? value.length > 0 : Boolean(value),
  ).length;

const defaultSortDirection = (
  field: PaymentSortField,
): PaymentSortDirection =>
  field === "paymentDate" || field === "amount" ? "desc" : "asc";

export const PaymentListPage = () => {
  const [draftFilters, setDraftFilters] =
    useState<PaymentFilters>(copyEmptyFilters);
  const [filters, setFilters] =
    useState<PaymentFilters>(copyEmptyFilters);
  const [pageSize, setPageSize] = useState(25);
  const [pagePosition, setPagePosition] = useState(1);
  const [sortBy, setSortBy] =
    useState<PaymentSortField>("paymentDate");
  const [sortDirection, setSortDirection] =
    useState<PaymentSortDirection>("desc");
  const [cursor, setCursor] = useState<{
    after?: string;
    before?: string;
  }>({});
  const [refundPayment, setRefundPayment] =
    useState<PaymentListItem | null>(null);

  const query = useMemo<PaymentQuery>(
    () => ({
      pageSize,
      filters,
      sortBy,
      sortDirection,
      ...cursor,
    }),
    [cursor, filters, pageSize, sortBy, sortDirection],
  );

  const {
    data,
    error,
    isError,
    isFetching,
    isLoading,
    dataUpdatedAt,
    refetch,
  } = usePayments(query);

  const resetPagination = () => {
    setCursor({});
    setPagePosition(1);
  };

  const applyFilters = () => {
    setFilters({
      ...draftFilters,
      providerNames: [...draftFilters.providerNames],
      paymentStatuses: [...draftFilters.paymentStatuses],
    });
    resetPagination();
  };

  const resetFilters = () => {
    const emptyFilters = copyEmptyFilters();
    setDraftFilters(emptyFilters);
    setFilters(copyEmptyFilters());
    resetPagination();
  };

  const changeSort = (field: PaymentSortField) => {
    if (field === sortBy) {
      setSortDirection((current) => (current === "asc" ? "desc" : "asc"));
    } else {
      setSortBy(field);
      setSortDirection(defaultSortDirection(field));
    }

    resetPagination();
  };

  const goToNextPage = () => {
    if (!data?.pageInfo.endCursor) {
      return;
    }

    setCursor({ after: data.pageInfo.endCursor });
    setPagePosition((current) => current + 1);
  };

  const goToPreviousPage = () => {
    if (!data?.pageInfo.startCursor) {
      return;
    }

    setCursor({ before: data.pageInfo.startCursor });
    setPagePosition((current) => Math.max(1, current - 1));
  };

  const activeFilterCount = countActiveFilters(filters);
  const draftActiveFilterCount = countActiveFilters(draftFilters);
  const items = data?.items ?? [];
  const hasInitialError = isError && !data;
  const lastUpdated = dataUpdatedAt
    ? new Intl.DateTimeFormat(undefined, {
        hour: "2-digit",
        minute: "2-digit",
      }).format(new Date(dataUpdatedAt))
    : null;

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <section className="relative overflow-hidden rounded-2xl border bg-gradient-to-br from-blocks-primary-shades-100 via-card to-blocks-secondary-50 p-5 shadow-sm sm:p-7">
        <div className="absolute -right-16 -top-20 h-52 w-52 rounded-full bg-blocks-primary-100/30 blur-3xl" />
        <div className="relative flex flex-col justify-between gap-5 sm:flex-row sm:items-center">
          <div className="flex items-start gap-4">
            <div className="rounded-xl bg-blocks-primary-600 p-3 text-white shadow-sm">
              <CreditCard className="h-6 w-6" />
            </div>
            <div>
              <div className="flex flex-wrap items-center gap-2">
                <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">
                  Payment list
                </h1>
                <span className="inline-flex items-center gap-1.5 rounded-full border border-blocks-secondary-200 bg-blocks-secondary-50 px-2.5 py-1 text-xs font-medium text-blocks-secondary-800">
                  <span className="h-1.5 w-1.5 rounded-full bg-blocks-secondary-500" />
                  Live
                </span>
              </div>
              <p className="mt-1 max-w-2xl text-sm text-muted-foreground sm:text-base">
                Monitor payment activity, outcomes, providers, and amounts for
                the current tenant.
              </p>
              {lastUpdated && (
                <p className="mt-2 text-xs text-muted-foreground">
                  Last refreshed at {lastUpdated}
                </p>
              )}
            </div>
          </div>
          <div className="flex flex-wrap gap-2 self-start sm:self-center">
            <Button
              variant="outline"
              className="bg-background/80"
              onClick={() => refetch()}
              disabled={isFetching}
            >
              <RefreshCw
                className={`mr-2 h-4 w-4 ${isFetching ? "animate-spin" : ""}`}
              />
              Refresh
            </Button>
          </div>
        </div>
      </section>

      <PaymentFiltersPanel
        value={draftFilters}
        activeFilterCount={draftActiveFilterCount}
        onChange={setDraftFilters}
        onApply={applyFilters}
        onReset={resetFilters}
      />

      <Card className="rounded-xl p-0">
        <div className="flex flex-col gap-3 border-b px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-5">
          <div>
            <h2 className="font-semibold">Payment activity</h2>
            <p className="text-xs text-muted-foreground">
              {items.length > 0
                ? `${items.length} payment${items.length === 1 ? "" : "s"} on this page`
                : "Current payment records"}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <label
              htmlFor="payment-page-size"
              className="text-xs text-muted-foreground"
            >
              Rows per page
            </label>
            <Select
              value={pageSize.toString()}
              onValueChange={(value) => {
                setPageSize(Number(value));
                resetPagination();
              }}
            >
              <SelectTrigger id="payment-page-size" className="h-9 w-20">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {PAYMENT_PAGE_SIZE_OPTIONS.map((option) => (
                  <SelectItem key={option} value={option.toString()}>
                    {option}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </div>

        <div className="p-4 sm:p-5">
          {isLoading ? (
            <PaymentListSkeleton />
          ) : hasInitialError ? (
            <div className="flex min-h-72 flex-col items-center justify-center px-4 text-center">
              <span className="rounded-full bg-destructive/10 p-4 text-destructive">
                <AlertCircle className="h-7 w-7" />
              </span>
              <h3 className="mt-4 text-lg font-semibold">
                Payments could not be loaded
              </h3>
              <p className="mt-1 max-w-md text-sm text-muted-foreground">
                {error instanceof Error
                  ? error.message
                  : "Check your connection and try again."}
              </p>
              <Button className="mt-5" onClick={() => refetch()}>
                Try again
              </Button>
            </div>
          ) : items.length === 0 ? (
            <div className="flex min-h-72 flex-col items-center justify-center px-4 text-center">
              <span className="rounded-full bg-muted p-4 text-muted-foreground">
                <SearchX className="h-7 w-7" />
              </span>
              <h3 className="mt-4 text-lg font-semibold">
                {activeFilterCount > 0
                  ? "No matching payments"
                  : "No payments yet"}
              </h3>
              <p className="mt-1 max-w-md text-sm text-muted-foreground">
                {activeFilterCount > 0
                  ? "Try adjusting or clearing the current filters."
                  : "Payments will appear here as soon as they are created."}
              </p>
              {activeFilterCount > 0 && (
                <Button
                  variant="outline"
                  className="mt-5"
                  onClick={resetFilters}
                >
                  Clear filters
                </Button>
              )}
            </div>
          ) : (
            <div className={isFetching ? "opacity-70 transition-opacity" : ""}>
              <PaymentTable
                items={items}
                sortBy={sortBy}
                sortDirection={sortDirection}
                onSort={changeSort}
                onRefund={setRefundPayment}
              />
            </div>
          )}
        </div>

        {data && items.length > 0 && (
          <div className="flex flex-col gap-3 border-t px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-5">
            <p className="text-sm text-muted-foreground">
              Page{" "}
              <span className="font-medium text-foreground">
                {pagePosition}
              </span>
            </p>
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={goToPreviousPage}
                disabled={!data.pageInfo.hasPreviousPage || isFetching}
              >
                <ChevronLeft className="mr-1 h-4 w-4" />
                Previous
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={goToNextPage}
                disabled={!data.pageInfo.hasNextPage || isFetching}
              >
                Next
                <ChevronRight className="ml-1 h-4 w-4" />
              </Button>
            </div>
          </div>
        )}
      </Card>

      {refundPayment && (
        <PaymentRefundDialog
          payment={refundPayment}
          onClose={() => setRefundPayment(null)}
        />
      )}
    </main>
  );
};
