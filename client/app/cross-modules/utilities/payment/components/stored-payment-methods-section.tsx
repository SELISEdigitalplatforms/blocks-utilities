import { useEffect, useMemo, useState } from "react";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import {
  AlertCircle,
  ChevronLeft,
  ChevronRight,
  CreditCard,
  Loader2,
  Search,
  ShieldCheck,
  Trash2,
} from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { toast } from "@/hooks/use-toast";
import {
  useRemoveStoredPaymentMethod,
  useStoredPaymentMethods,
} from "../hooks/use-stored-payment-methods";
import type { StoredPaymentMethod } from "../models/stored-payment-method.model";
import { STORED_PAYMENT_METHOD_PAGE_SIZE_OPTIONS } from "../constants/payment.constants";
import { StoredPaymentMethodTable } from "./stored-payment-method-table";

const ALL_FILTER_VALUE = "all";

/**
 * Radix rejects an empty string as a Select value, so the "own organization" choice needs a
 * sentinel of its own rather than "".
 */
const OWN_ORGANIZATION_VALUE = "own";

const ORGANIZATION_PAGE_SIZE = 200;

const normalize = (value: string | null) =>
  value?.trim().toLowerCase() || "";

const StoredPaymentMethodsSkeleton = () => {
  const { t } = useT("utilities.payment");
  return (
  <div aria-label={t("STORED_PAYMENT_METHODS_SECTION.LOADING_SAVED_PAYMENT_METHODS")} className="space-y-3">
    <div className="grid gap-3 sm:grid-cols-3">
      <Skeleton className="h-10" />
      <Skeleton className="h-10" />
      <Skeleton className="h-10" />
    </div>
    <div className="hidden overflow-hidden rounded-xl border md:block">
      {[0, 1, 2].map((row) => (
        <div
          key={row}
          className="grid grid-cols-4 gap-4 border-b p-4 last:border-0"
        >
          <Skeleton className="h-10" />
          <Skeleton className="h-10" />
          <Skeleton className="h-10" />
          <Skeleton className="h-10" />
        </div>
      ))}
    </div>
    <Skeleton className="h-40 md:hidden" />
  </div>
);
};

export const StoredPaymentMethodsSection = () => {
  const { t } = useT("utilities.payment");
  // A card is stamped with the organization that saved it, and the console is fixed to one
  // organization, so the cards from payments taken for another are only reachable by naming it.
  const [organizationValue, setOrganizationValue] = useState(
    OWN_ORGANIZATION_VALUE,
  );
  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: ORGANIZATION_PAGE_SIZE,
  });
  const organizations = organizationsData?.organizations ?? [];
  const {
    data: methods = [],
    error,
    isError,
    isLoading,
    isFetching,
    refetch,
  } = useStoredPaymentMethods(
    organizationValue === OWN_ORGANIZATION_VALUE
      ? undefined
      : organizationValue,
  );
  const {
    mutateAsync: removeMethod,
    isPending: isRemoving,
    variables: removingPaymentMethodId,
  } = useRemoveStoredPaymentMethod();

  const [searchText, setSearchText] = useState("");
  const [brandFilter, setBrandFilter] = useState(ALL_FILTER_VALUE);
  const [typeFilter, setTypeFilter] = useState(ALL_FILTER_VALUE);
  const [pageSize, setPageSize] = useState(5);
  const [page, setPage] = useState(1);
  const [selectedMethod, setSelectedMethod] =
    useState<StoredPaymentMethod | null>(null);

  const brands = useMemo(
    () =>
      Array.from(
        new Set(
          methods
            .map((method) => method.brand?.trim())
            .filter((brand): brand is string => Boolean(brand)),
        ),
      ).sort((left, right) => left.localeCompare(right)),
    [methods],
  );

  const types = useMemo(
    () =>
      Array.from(
        new Set(
          methods
            .map((method) => method.type?.trim())
            .filter((type): type is string => Boolean(type)),
        ),
      ).sort((left, right) => left.localeCompare(right)),
    [methods],
  );

  const filteredMethods = useMemo(() => {
    const search = normalize(searchText);

    return methods.filter((method) => {
      const matchesSearch =
        !search ||
        normalize(method.brand).includes(search) ||
        normalize(method.lastFour).includes(search) ||
        normalize(method.type).includes(search);
      const matchesBrand =
        brandFilter === ALL_FILTER_VALUE ||
        method.brand === brandFilter;
      const matchesType =
        typeFilter === ALL_FILTER_VALUE ||
        method.type === typeFilter;

      return matchesSearch && matchesBrand && matchesType;
    });
  }, [brandFilter, methods, searchText, typeFilter]);

  const totalPages = Math.max(
    1,
    Math.ceil(filteredMethods.length / pageSize),
  );

  useEffect(() => {
    setPage((current) => Math.min(current, totalPages));
  }, [totalPages]);

  useEffect(() => {
    setPage(1);
  }, [brandFilter, pageSize, searchText, typeFilter]);

  const pageMethods = filteredMethods.slice(
    (page - 1) * pageSize,
    page * pageSize,
  );

  const hasFilters =
    Boolean(searchText.trim()) ||
    brandFilter !== ALL_FILTER_VALUE ||
    typeFilter !== ALL_FILTER_VALUE;

  const clearFilters = () => {
    setSearchText("");
    setBrandFilter(ALL_FILTER_VALUE);
    setTypeFilter(ALL_FILTER_VALUE);
  };

  const confirmRemoval = async () => {
    if (!selectedMethod) {
      return;
    }

    try {
      const outcome = await removeMethod(
        selectedMethod.paymentMethodId,
      );

      toast({
        variant: outcome === "removed" ? "success" : "info",
        title:
          outcome === "removed"
            ? t("STORED_PAYMENT_METHODS_SECTION.PAYMENT_METHOD_REMOVED")
            : t("STORED_PAYMENT_METHODS_SECTION.REMOVAL_IS_PROCESSING"),
        description:
          outcome === "removed"
            ? t("STORED_PAYMENT_METHODS_SECTION.THE_SAVED_PAYMENT_METHOD_IS")
            : t("STORED_PAYMENT_METHODS_SECTION.THE_METHOD_IS_BLOCKED_LOCALLY"),
      });
      setSelectedMethod(null);
    } catch (removalError) {
      toast({
        variant: "destructive",
        title: t("STORED_PAYMENT_METHODS_SECTION.REMOVAL_FAILED"),
        description:
          removalError instanceof Error
            ? removalError.message
            : t("STORED_PAYMENT_METHODS_SECTION.THE_SAVED_PAYMENT_METHOD_COULD"),
      });
    }
  };

  return (
    <>
      <Card className="rounded-xl p-0">
        <div className="flex flex-col gap-3 border-b px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-5">
          <div className="flex items-start gap-3">
            <span className="rounded-lg bg-blocks-primary-shades-200 p-2 text-blocks-primary-600">
              <ShieldCheck className="h-5 w-5" />
            </span>
            <div>
              <h2 className="font-semibold">{t("STORED_PAYMENT_METHODS_SECTION.SAVED_PAYMENT_METHODS")}</h2>
              <p className="text-xs text-muted-foreground">
                {t("STORED_PAYMENT_METHODS_SECTION.ACTIVE_METHODS_BELONGING_TO_THE")}
              </p>
            </div>
          </div>

          <div className="flex items-center gap-3">
            {/* Outside the loading and error branches below on purpose: a failed load for one
                organization must still leave a way to switch to another. */}
            <Select
              value={organizationValue}
              onValueChange={(nextValue) => {
                setOrganizationValue(nextValue);
                setPage(1);
              }}
            >
              <SelectTrigger
                aria-label={t("STORED_PAYMENT_METHODS_SECTION.ORGANIZATION")}
                className="w-56"
              >
                <SelectValue placeholder={t("STORED_PAYMENT_METHODS_SECTION.MY_ORGANIZATION")} />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={OWN_ORGANIZATION_VALUE}>
                  {t("STORED_PAYMENT_METHODS_SECTION.MY_ORGANIZATION")}
                </SelectItem>
                {organizations.map((organization) => (
                  <SelectItem
                    key={organization.itemId}
                    value={organization.itemId}
                  >
                    {organization.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            {!isLoading && !isError && (
              <p className="whitespace-nowrap text-sm text-muted-foreground">
                {t("STORED_PAYMENT_METHODS_SECTION.METHODS_COUNT", { shown: filteredMethods.length, total: methods.length })}
              </p>
            )}
          </div>
        </div>

        <div className="p-4 sm:p-5">
          {isLoading ? (
            <StoredPaymentMethodsSkeleton />
          ) : isError ? (
            <div className="flex min-h-56 flex-col items-center justify-center px-4 text-center">
              <span className="rounded-full bg-destructive/10 p-4 text-destructive">
                <AlertCircle className="h-6 w-6" />
              </span>
              <h3 className="mt-4 font-semibold">
                {t("STORED_PAYMENT_METHODS_SECTION.SAVED_METHODS_COULD_NOT_BE")}
              </h3>
              <p className="mt-1 max-w-md text-sm text-muted-foreground">
                {error instanceof Error
                  ? error.message
                  : t("STORED_PAYMENT_METHODS_SECTION.CHECK_YOUR_CONNECTION_AND_TRY")}
              </p>
              <Button className="mt-4" onClick={() => refetch()}>
                {t("STORED_PAYMENT_METHODS_SECTION.TRY_AGAIN")}
              </Button>
            </div>
          ) : (
            <>
              <div className="mb-5 grid gap-3 sm:grid-cols-2 xl:grid-cols-[minmax(15rem,1fr)_14rem_14rem_auto]">
                <div className="relative sm:col-span-2 xl:col-span-1">
                  <Search className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
                  <Input
                    value={searchText}
                    className="pl-9"
                    placeholder={t("STORED_PAYMENT_METHODS_SECTION.SEARCH_BRAND_OR_LAST_FOUR")}
                    aria-label={t("STORED_PAYMENT_METHODS_SECTION.SEARCH_SAVED_PAYMENT_METHODS")}
                    onChange={(event) =>
                      setSearchText(event.target.value.slice(0, 50))
                    }
                  />
                </div>

                <Select
                  value={brandFilter}
                  onValueChange={setBrandFilter}
                >
                  <SelectTrigger aria-label={t("STORED_PAYMENT_METHODS_SECTION.FILTER_BY_CARD_BRAND")}>
                    <SelectValue placeholder={t("STORED_PAYMENT_METHODS_SECTION.ALL_BRANDS")} />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={ALL_FILTER_VALUE}>
                      {t("STORED_PAYMENT_METHODS_SECTION.ALL_BRANDS")}
                    </SelectItem>
                    {brands.map((brand) => (
                      <SelectItem key={brand} value={brand}>
                        {brand}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>

                <Select
                  value={typeFilter}
                  onValueChange={setTypeFilter}
                >
                  <SelectTrigger aria-label={t("STORED_PAYMENT_METHODS_SECTION.FILTER_BY_METHOD_TYPE")}>
                    <SelectValue placeholder={t("STORED_PAYMENT_METHODS_SECTION.ALL_TYPES")} />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={ALL_FILTER_VALUE}>
                      {t("STORED_PAYMENT_METHODS_SECTION.ALL_TYPES")}
                    </SelectItem>
                    {types.map((type) => (
                      <SelectItem key={type} value={type}>
                        {type}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>

                <Button
                  type="button"
                  variant="outline"
                  disabled={!hasFilters}
                  onClick={clearFilters}
                >
                  {t("STORED_PAYMENT_METHODS_SECTION.CLEAR_FILTERS")}
                </Button>
              </div>

              {pageMethods.length === 0 ? (
                <div className="flex min-h-56 flex-col items-center justify-center px-4 text-center">
                  <span className="rounded-full bg-muted p-4 text-muted-foreground">
                    <CreditCard className="h-6 w-6" />
                  </span>
                  <h3 className="mt-4 font-semibold">
                    {hasFilters
                      ? t("STORED_PAYMENT_METHODS_SECTION.NO_MATCHING_PAYMENT_METHODS")
                      : t("STORED_PAYMENT_METHODS_SECTION.NO_SAVED_PAYMENT_METHODS")}
                  </h3>
                  <p className="mt-1 max-w-md text-sm text-muted-foreground">
                    {hasFilters
                      ? t("STORED_PAYMENT_METHODS_SECTION.TRY_CHANGING_OR_CLEARING_THE")
                      : t("STORED_PAYMENT_METHODS_SECTION.A_METHOD_APPEARS_HERE_AFTER")}
                  </p>
                  {hasFilters && (
                    <Button
                      type="button"
                      variant="outline"
                      className="mt-4"
                      onClick={clearFilters}
                    >
                      {t("STORED_PAYMENT_METHODS_SECTION.CLEAR_FILTERS")}
                    </Button>
                  )}
                </div>
              ) : (
                <div
                  className={
                    isFetching
                      ? "opacity-70 transition-opacity"
                      : ""
                  }
                >
                  <StoredPaymentMethodTable
                    methods={pageMethods}
                    removingPaymentMethodId={
                      isRemoving
                        ? removingPaymentMethodId ?? null
                        : null
                    }
                    onRemove={setSelectedMethod}
                  />
                </div>
              )}
            </>
          )}
        </div>

        {!isLoading && !isError && filteredMethods.length > 0 && (
          <div className="flex flex-col gap-3 border-t px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-5">
            <div className="flex items-center gap-2">
              <label
                htmlFor="stored-method-page-size"
                className="text-xs text-muted-foreground"
              >
                {t("STORED_PAYMENT_METHODS_SECTION.ROWS_PER_PAGE")}
              </label>
              <Select
                value={pageSize.toString()}
                onValueChange={(value) => setPageSize(Number(value))}
              >
                <SelectTrigger
                  id="stored-method-page-size"
                  className="h-9 w-20"
                >
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {STORED_PAYMENT_METHOD_PAGE_SIZE_OPTIONS.map(
                    (option) => (
                      <SelectItem
                        key={option}
                        value={option.toString()}
                      >
                        {option}
                      </SelectItem>
                    ),
                  )}
                </SelectContent>
              </Select>
            </div>

            <div className="flex items-center justify-between gap-3 sm:justify-end">
              <p className="text-sm text-muted-foreground">
                {t("STORED_PAYMENT_METHODS_SECTION.PAGE_OF", { page, total: totalPages })}
              </p>
              <div className="flex gap-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={page <= 1}
                  aria-label={t("STORED_PAYMENT_METHODS_SECTION.PREVIOUS_SAVED_METHOD_PAGE")}
                  onClick={() =>
                    setPage((current) => Math.max(1, current - 1))
                  }
                >
                  <ChevronLeft className="h-4 w-4" />
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={page >= totalPages}
                  aria-label={t("STORED_PAYMENT_METHODS_SECTION.NEXT_SAVED_METHOD_PAGE")}
                  onClick={() =>
                    setPage((current) =>
                      Math.min(totalPages, current + 1),
                    )
                  }
                >
                  <ChevronRight className="h-4 w-4" />
                </Button>
              </div>
            </div>
          </div>
        )}
      </Card>

      <Dialog
        open={Boolean(selectedMethod)}
        onOpenChange={(open) => {
          if (!open && !isRemoving) {
            setSelectedMethod(null);
          }
        }}
      >
        <DialogContent hideCloseButton={isRemoving}>
          <DialogHeader>
            <DialogTitle>{t("STORED_PAYMENT_METHODS_SECTION.REMOVE_SAVED_PAYMENT_METHOD")}</DialogTitle>
            <DialogDescription>
              {t("STORED_PAYMENT_METHODS_SECTION.REMOVE_CONFIRM", {
                brand: selectedMethod?.brand ?? t("STORED_PAYMENT_METHODS_SECTION.THIS_PAYMENT_METHOD"),
                lastFour: selectedMethod?.lastFour ?? t("STORED_PAYMENT_METHODS_SECTION.UNKNOWN_DIGITS"),
              })}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              disabled={isRemoving}
              onClick={() => setSelectedMethod(null)}
            >
              {t("STORED_PAYMENT_METHODS_SECTION.KEEP_METHOD")}
            </Button>
            <Button
              type="button"
              variant="destructive"
              disabled={isRemoving}
              onClick={confirmRemoval}
            >
              {isRemoving ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <Trash2 className="mr-2 h-4 w-4" />
              )}
              {isRemoving ? t("STORED_PAYMENT_METHODS_SECTION.REMOVING") : t("STORED_PAYMENT_METHODS_SECTION.REMOVE_METHOD")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
};
