import {
  ArrowDown,
  ArrowUp,
  ArrowUpDown,
  CalendarDays,
  Clock3,
  CreditCard,
  RotateCcw,
} from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { cn } from "@/lib/utils";
import { REFUNDABLE_PAYMENT_STATUSES } from "../constants/payment.constants";
import {
  type PaymentListItem,
  type PaymentSortDirection,
  type PaymentSortField,
} from "../models/payment.model";
import { PaymentStatusBadge } from "./payment-status-badge";

interface PaymentTableProps {
  items: PaymentListItem[];
  sortBy: PaymentSortField;
  sortDirection: PaymentSortDirection;
  onSort: (field: PaymentSortField) => void;
  onRefund: (payment: PaymentListItem) => void;
}

interface SortableHeaderProps {
  label: string;
  field: PaymentSortField;
  activeField: PaymentSortField;
  direction: PaymentSortDirection;
  align?: "left" | "right";
  onSort: (field: PaymentSortField) => void;
}

interface RefundActionProps {
  payment: PaymentListItem;
  onRefund: (payment: PaymentListItem) => void;
}

const formatAmount = (amount: number, currencyCode: string) => {
  try {
    return new Intl.NumberFormat(undefined, {
      style: "currency",
      currency: currencyCode,
      currencyDisplay: "code",
    }).format(amount);
  } catch {
    return `${amount.toFixed(2)} ${currencyCode}`;
  }
};

const formatPaymentDate = (value: string) => {
  const date = new Date(value);

  if (Number.isNaN(date.getTime())) {
    return "—";
  }

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(date);
};

const shortenPaymentId = (paymentDetailId: string) =>
  paymentDetailId.length > 18
    ? `${paymentDetailId.slice(0, 10)}…${paymentDetailId.slice(-6)}`
    : paymentDetailId;

const SortableHeader = ({
  label,
  field,
  activeField,
  direction,
  align = "left",
  onSort,
}: SortableHeaderProps) => {
  const active = activeField === field;
  const Icon = !active
    ? ArrowUpDown
    : direction === "asc"
      ? ArrowUp
      : ArrowDown;

  return (
    <Button
      type="button"
      variant="ghost"
      size="sm"
      className={cn(
        "-ml-3 h-8 px-3 text-xs font-semibold uppercase tracking-wide text-muted-foreground",
        align === "right" && "ml-auto -mr-3",
        active && "text-foreground",
      )}
      onClick={() => onSort(field)}
    >
      {label}
      <Icon className="ml-1.5 h-3.5 w-3.5" />
    </Button>
  );
};

const RefundAction = ({
  payment,
  onRefund,
}: RefundActionProps) => {
  const hasPendingRefund = payment.hasPendingRefund === true;
  const canRefund =
    !hasPendingRefund &&
    REFUNDABLE_PAYMENT_STATUSES.has(payment.paymentStatus);

  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      disabled={!canRefund}
      title={
        hasPendingRefund
          ? "The refund request is awaiting provider confirmation"
          : canRefund
            ? "Refund this payment"
            : "This payment is not refundable in its current status"
      }
      onClick={() => onRefund(payment)}
    >
      {hasPendingRefund ? (
        <Clock3 className="mr-1.5 h-3.5 w-3.5" />
      ) : (
        <RotateCcw className="mr-1.5 h-3.5 w-3.5" />
      )}
      {hasPendingRefund ? "Refund requested" : "Refund"}
    </Button>
  );
};

export const PaymentTable = ({
  items,
  sortBy,
  sortDirection,
  onSort,
  onRefund,
}: PaymentTableProps) => (
  <>
    <div className="hidden overflow-hidden rounded-xl border md:block">
      <Table>
        <TableHeader className="bg-muted/40">
          <TableRow>
            <TableHead>Payment ID</TableHead>
            <TableHead>
              <SortableHeader
                label="Provider"
                field="providerName"
                activeField={sortBy}
                direction={sortDirection}
                onSort={onSort}
              />
            </TableHead>
            <TableHead className="text-right">
              <SortableHeader
                label="Amount"
                field="amount"
                activeField={sortBy}
                direction={sortDirection}
                align="right"
                onSort={onSort}
              />
            </TableHead>
            <TableHead>
              <SortableHeader
                label="Payment date"
                field="paymentDate"
                activeField={sortBy}
                direction={sortDirection}
                onSort={onSort}
              />
            </TableHead>
            <TableHead>
              <SortableHeader
                label="Status"
                field="paymentStatus"
                activeField={sortBy}
                direction={sortDirection}
                onSort={onSort}
              />
            </TableHead>
            <TableHead className="text-right">Actions</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {items.map((payment) => (
            <TableRow
              key={payment.paymentDetailId}
              className="transition-colors hover:bg-muted/30"
            >
              <TableCell>
                <span
                  title={payment.paymentDetailId}
                  className="font-mono text-xs text-muted-foreground"
                >
                  {shortenPaymentId(payment.paymentDetailId)}
                </span>
              </TableCell>
              <TableCell>
                <div className="flex items-center gap-2.5">
                  <span className="rounded-lg bg-blocks-primary-shades-200 p-2 text-blocks-primary-600">
                    <CreditCard className="h-4 w-4" />
                  </span>
                  <span className="font-medium">{payment.providerName}</span>
                </div>
              </TableCell>
              <TableCell className="text-right font-semibold tabular-nums">
                {formatAmount(payment.amount, payment.currencyCode)}
              </TableCell>
              <TableCell>
                <div className="flex items-center gap-2 text-sm">
                  <CalendarDays className="h-4 w-4 text-muted-foreground" />
                  {formatPaymentDate(payment.paymentDateUtc)}
                </div>
              </TableCell>
              <TableCell>
                <PaymentStatusBadge status={payment.paymentStatus} />
              </TableCell>
              <TableCell className="text-right">
                <RefundAction
                  payment={payment}
                  onRefund={onRefund}
                />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>

    <div className="grid gap-3 md:hidden">
      {items.map((payment) => (
        <article
          key={payment.paymentDetailId}
          className="rounded-xl border bg-card p-4 shadow-sm"
        >
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <p className="truncate font-semibold">{payment.providerName}</p>
              <p
                title={payment.paymentDetailId}
                className="mt-1 truncate font-mono text-xs text-muted-foreground"
              >
                {shortenPaymentId(payment.paymentDetailId)}
              </p>
            </div>
            <PaymentStatusBadge status={payment.paymentStatus} />
          </div>
          <p className="mt-5 text-xl font-bold tabular-nums">
            {formatAmount(payment.amount, payment.currencyCode)}
          </p>
          <div className="mt-4 flex items-center justify-between gap-3 border-t pt-3">
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <CalendarDays className="h-4 w-4" />
              {formatPaymentDate(payment.paymentDateUtc)}
            </div>
            <RefundAction payment={payment} onRefund={onRefund} />
          </div>
        </article>
      ))}
    </div>
  </>
);
