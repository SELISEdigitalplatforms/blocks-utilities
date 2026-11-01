import { Badge } from "@/components/ui-kits/badge/badge";
import { MagicUrl } from "@blocks-utilities/magic-url/models/magic-url.model";
import { tStatic } from "@seliseblocks/genesis-os";

const getStatusFallback = (item: MagicUrl) => {
  const now = new Date();
  const expiryDate = item.expiryDate ? new Date(item.expiryDate) : null;

  if (item.expiredReason === "ManuallyDisabled") {
    return { label: tStatic("utilities.magic-url", "common:DISABLED"), variant: "error" };
  }

  if (
    item.expiredReason === "UsageLimitExceeded" ||
    (item.usageLimit > 0 && item.usageCount >= item.usageLimit)
  ) {
    return { label: tStatic("utilities.magic-url", "MAGIC_URL_STATUS_BADGE.LIMIT_EXCEEDED"), variant: "error" };
  }

  if (item.expiredReason === "TimeExpired" || (expiryDate && expiryDate < now)) {
    return { label: tStatic("utilities.magic-url", "MAGIC_URL_STATUS_BADGE.EXPIRED"), variant: "error" };
  }

  if (item.isExpired) {
    return { label: tStatic("utilities.magic-url", "MAGIC_URL_STATUS_BADGE.EXPIRED"), variant: "error" };
  }

  return { label: tStatic("utilities.magic-url", "MAGIC_URL_STATUS_BADGE.UNKNOWN"), variant: "info" };
};

const getStatus = (item: MagicUrl) => {
  if (item.status) {
    switch (item.status) {
      case "Active":
        return { label: tStatic("utilities.magic-url", "MAGIC_URL_STATUS_BADGE.ACTIVE"), variant: "success" };
      case "Inactive":
      case "Disabled":
        return {
          label:
            item.status === "Inactive"
              ? tStatic("utilities.magic-url", "MAGIC_URL_STATUS_BADGE.INACTIVE")
              : tStatic("utilities.magic-url", "common:DISABLED"),
          variant: "error",
        };
      case "Expired":
        return { label: tStatic("utilities.magic-url", "MAGIC_URL_STATUS_BADGE.EXPIRED"), variant: "error" };
    }
  }

  return getStatusFallback(item);
};

export function MagicUrlStatusBadge({ item }: { item: MagicUrl }) {
  const { label, variant } = getStatus(item);

  return (
    <Badge variant={variant as "success" | "error" | "info" | "secondary"} className="whitespace-nowrap rounded-full">
      {label}
    </Badge>
  );
}
