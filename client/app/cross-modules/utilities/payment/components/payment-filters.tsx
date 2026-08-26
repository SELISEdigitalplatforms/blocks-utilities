import { useState } from "react";
import type { FormEvent } from "react";
import { ChevronDown, Filter, RotateCcw } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui-kits/collapsible/collapsible";
import { Input } from "@/components/ui-kits/input/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { cn } from "@/lib/utils";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/genesis-os";
import {
  PAYMENT_CURRENCY_OPTIONS,
  PAYMENT_FLOW_OPTIONS,
  PAYMENT_PROVIDER_SUGGESTIONS,
  PAYMENT_STATUS_OPTIONS,
} from "../constants/payment.constants";
import type { PaymentFilters } from "../models/payment.model";
import { PaymentMultiSelect } from "./payment-multi-select";

interface PaymentFiltersPanelProps {
  value: PaymentFilters;
  activeFilterCount: number;
  onChange: (value: PaymentFilters) => void;
  onApply: () => void;
  onReset: () => void;
}

const ORGANIZATION_PAGE_SIZE = 200;

const validateFilters = (filters: PaymentFilters): string | null => {
  const minAmount = filters.minAmount
    ? Number(filters.minAmount)
    : undefined;
  const maxAmount = filters.maxAmount
    ? Number(filters.maxAmount)
    : undefined;

  if (
    minAmount !== undefined &&
    maxAmount !== undefined &&
    minAmount > maxAmount
  ) {
    return "Maximum amount must be greater than or equal to minimum amount.";
  }

  if (
    filters.paymentDateFrom &&
    filters.paymentDateTo &&
    filters.paymentDateFrom > filters.paymentDateTo
  ) {
    return "The end date must be the same as or later than the start date.";
  }

  if (
    filters.currencyCode &&
    !/^[A-Z]{3}$/.test(filters.currencyCode)
  ) {
    return "Currency must contain exactly three letters.";
  }

  return null;
};

export const PaymentFiltersPanel = ({
  value,
  activeFilterCount,
  onChange,
  onApply,
  onReset,
}: PaymentFiltersPanelProps) => {
  const [expanded, setExpanded] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);

  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: ORGANIZATION_PAGE_SIZE,
  });
  const organizations = organizationsData?.organizations ?? [];

  const update = <Key extends keyof PaymentFilters>(
    key: Key,
    nextValue: PaymentFilters[Key],
  ) => {
    setValidationError(null);
    onChange({ ...value, [key]: nextValue });
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const error = validateFilters(value);
    setValidationError(error);

    if (!error) {
      onApply();
    }
  };

  return (
    <form
      onSubmit={submit}
      className="rounded-xl border bg-card p-4 shadow-sm sm:p-5"
    >
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <div className="rounded-lg bg-blocks-primary-shades-200 p-2 text-blocks-primary-600">
            <Filter className="h-4 w-4" />
          </div>
          <div>
            <h2 className="text-sm font-semibold">Filter payments</h2>
            <p className="text-xs text-muted-foreground">
              Narrow the list using exact payment data.
            </p>
          </div>
          {activeFilterCount > 0 && (
            <span className="rounded-full bg-primary px-2 py-0.5 text-xs font-medium text-primary-foreground">
              {activeFilterCount}
            </span>
          )}
        </div>

        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="text-muted-foreground"
          onClick={onReset}
          disabled={activeFilterCount === 0}
        >
          <RotateCcw className="mr-2 h-4 w-4" />
          Reset
        </Button>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <PaymentMultiSelect
          label="Providers"
          values={value.providerNames}
          options={PAYMENT_PROVIDER_SUGGESTIONS}
          emptyLabel="All providers"
          allowCustomValue
          onChange={(providerNames) => update("providerNames", providerNames)}
        />
        <PaymentMultiSelect
          label="Statuses"
          values={value.paymentStatuses}
          options={PAYMENT_STATUS_OPTIONS}
          emptyLabel="All statuses"
          onChange={(paymentStatuses) =>
            update("paymentStatuses", paymentStatuses)
          }
        />
        <div className="space-y-1.5">
          <label className="text-xs font-medium text-muted-foreground">
            Currency
          </label>
          <Select
            value={value.currencyCode || "all"}
            onValueChange={(currencyCode) =>
              update(
                "currencyCode",
                currencyCode === "all" ? "" : currencyCode,
              )
            }
          >
            <SelectTrigger>
              <SelectValue placeholder="All currencies" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All currencies</SelectItem>
              {PAYMENT_CURRENCY_OPTIONS.map((currency) => (
                <SelectItem
                  key={currency.code}
                  value={currency.code}
                >
                  {currency.code} — {currency.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1.5">
          <label className="text-xs font-medium text-muted-foreground">
            Payment flow
          </label>
          <Select
            value={value.paymentFlow || "all"}
            onValueChange={(paymentFlow) =>
              update(
                "paymentFlow",
                paymentFlow === "all" ? "" : paymentFlow,
              )
            }
          >
            <SelectTrigger>
              <SelectValue placeholder="All flows" />
            </SelectTrigger>
            <SelectContent>
              {PAYMENT_FLOW_OPTIONS.map((option) => (
                <SelectItem key={option.value} value={option.value}>
                  {option.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1.5">
          <label className="text-xs font-medium text-muted-foreground">
            Organization
          </label>
          <Select
            value={value.organizationId || "all"}
            onValueChange={(organizationId) =>
              update(
                "organizationId",
                organizationId === "all" ? "" : organizationId,
              )
            }
          >
            <SelectTrigger>
              <SelectValue placeholder="All organizations" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All organizations</SelectItem>
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
        </div>
      </div>

      <Collapsible open={expanded} onOpenChange={setExpanded}>
        <CollapsibleTrigger asChild>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="mt-3 px-0 text-primary hover:bg-transparent"
          >
            {expanded ? "Fewer filters" : "More filters"}
            <ChevronDown
              className={cn(
                "ml-2 h-4 w-4 transition-transform",
                expanded && "rotate-180",
              )}
            />
          </Button>
        </CollapsibleTrigger>
        <CollapsibleContent>
          <div className="mt-3 grid gap-4 border-t pt-4 sm:grid-cols-2 xl:grid-cols-4">
            <div className="space-y-1.5">
              <label
                htmlFor="payment-min-amount"
                className="text-xs font-medium text-muted-foreground"
              >
                Minimum amount
              </label>
              <Input
                id="payment-min-amount"
                type="number"
                min="0"
                step="any"
                value={value.minAmount}
                placeholder="0.00"
                onChange={(event) => update("minAmount", event.target.value)}
              />
            </div>
            <div className="space-y-1.5">
              <label
                htmlFor="payment-max-amount"
                className="text-xs font-medium text-muted-foreground"
              >
                Maximum amount
              </label>
              <Input
                id="payment-max-amount"
                type="number"
                min="0"
                step="any"
                value={value.maxAmount}
                placeholder="500.00"
                onChange={(event) => update("maxAmount", event.target.value)}
              />
            </div>
            <div className="space-y-1.5">
              <label
                htmlFor="payment-date-from"
                className="text-xs font-medium text-muted-foreground"
              >
                Payment date from
              </label>
              <Input
                id="payment-date-from"
                type="date"
                value={value.paymentDateFrom}
                onChange={(event) =>
                  update("paymentDateFrom", event.target.value)
                }
              />
            </div>
            <div className="space-y-1.5">
              <label
                htmlFor="payment-date-to"
                className="text-xs font-medium text-muted-foreground"
              >
                Payment date to
              </label>
              <Input
                id="payment-date-to"
                type="date"
                value={value.paymentDateTo}
                onChange={(event) =>
                  update("paymentDateTo", event.target.value)
                }
              />
            </div>
            <div className="space-y-1.5 sm:col-span-1 xl:col-span-2">
              <label
                htmlFor="payment-order-id"
                className="text-xs font-medium text-muted-foreground"
              >
                Order ID
              </label>
              <Input
                id="payment-order-id"
                value={value.orderId}
                maxLength={80}
                placeholder="Exact order ID"
                onChange={(event) => update("orderId", event.target.value)}
              />
            </div>
            <div className="space-y-1.5 sm:col-span-1 xl:col-span-2">
              <label
                htmlFor="payment-detail-id"
                className="text-xs font-medium text-muted-foreground"
              >
                Payment ID
              </label>
              <Input
                id="payment-detail-id"
                value={value.paymentDetailId}
                maxLength={128}
                placeholder="Exact payment detail ID"
                onChange={(event) =>
                  update("paymentDetailId", event.target.value)
                }
              />
            </div>
          </div>
        </CollapsibleContent>
      </Collapsible>

      {validationError && (
        <p role="alert" className="mt-3 text-sm text-destructive">
          {validationError}
        </p>
      )}

      <div className="mt-4 flex justify-end">
        <Button type="submit" className="min-w-28">
          Apply filters
        </Button>
      </div>
    </form>
  );
};
