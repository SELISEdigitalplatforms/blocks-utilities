import { Badge } from "@/components/ui-kits/badge/badge";
import { cn } from "@/lib/utils";

interface PaymentStatusBadgeProps {
  status: string;
}

const successStatuses = new Set([
  "AUTHORIZED",
  "CAPTURED",
  "REFUNDED",
]);

const errorStatuses = new Set([
  "MAKE_PAYMENT_FAILED",
  "REFUSED",
  "CANCELLED",
]);

const warningStatuses = new Set([
  "INITIATING",
  "PROCESSING",
  "INITIATION_UNKNOWN",
  "PARTIALLY_CAPTURED",
  "PARTIALLY_REFUNDED",
]);

const toReadableStatus = (status: string) =>
  status
    .toLowerCase()
    .split("_")
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");

export const PaymentStatusBadge = ({
  status,
}: PaymentStatusBadgeProps) => {
  const variant = successStatuses.has(status)
    ? "success"
    : errorStatuses.has(status)
      ? "error"
      : "info";

  return (
    <Badge
      variant={variant}
      className={cn(
        "inline-flex w-fit whitespace-nowrap rounded-full px-2.5 py-1 font-medium",
        warningStatuses.has(status) &&
          "border-warning-200 bg-warning-100 text-warning-900",
      )}
    >
      {toReadableStatus(status)}
    </Badge>
  );
};
