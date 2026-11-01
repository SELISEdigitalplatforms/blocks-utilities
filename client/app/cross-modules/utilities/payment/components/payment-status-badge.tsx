import { Badge } from "@/components/ui-kits/badge/badge";
import { cn } from "@/lib/utils";
import { useT, type TFunction } from "@seliseblocks/genesis-os";

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

const toReadableStatus = (status: string, t: TFunction) => {
  switch (status) {
    case "INITIATING":
      return t("PAYMENT_STATUS_BADGE.INITIATING");
    case "PROCESSING":
      return t("PAYMENT_STATUS_BADGE.PROCESSING");
    case "MAKE_PAYMENT_FAILED":
      return t("PAYMENT_STATUS_BADGE.MAKE_PAYMENT_FAILED");
    case "INITIATION_UNKNOWN":
      return t("PAYMENT_STATUS_BADGE.INITIATION_UNKNOWN");
    case "AUTHORIZED":
      return t("PAYMENT_STATUS_BADGE.AUTHORIZED");
    case "REFUSED":
      return t("PAYMENT_STATUS_BADGE.REFUSED");
    case "PARTIALLY_CAPTURED":
      return t("PAYMENT_STATUS_BADGE.PARTIALLY_CAPTURED");
    case "CAPTURED":
      return t("PAYMENT_STATUS_BADGE.CAPTURED");
    case "CANCELLED":
      return t("PAYMENT_STATUS_BADGE.CANCELLED");
    case "PARTIALLY_REFUNDED":
      return t("PAYMENT_STATUS_BADGE.PARTIALLY_REFUNDED");
    case "REFUNDED":
      return t("PAYMENT_STATUS_BADGE.REFUNDED");
    default:
      return status
        .toLowerCase()
        .split("_")
        .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
        .join(" ");
  }
};

export const PaymentStatusBadge = ({
  status,
}: PaymentStatusBadgeProps) => {
  const { t } = useT("utilities.payment");
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
      {toReadableStatus(status, t)}
    </Badge>
  );
};
