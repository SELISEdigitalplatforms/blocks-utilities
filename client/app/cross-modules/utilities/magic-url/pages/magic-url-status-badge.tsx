import { Badge } from "@/components/ui-kits/badge/badge";
import { MagicUrl } from "@blocks-utilities/magic-url/models/magic-url.model";

const getStatusFallback = (item: MagicUrl) => {
  const now = new Date();
  const expiryDate = item.expiryDate ? new Date(item.expiryDate) : null;

  if (item.expiredReason === "ManuallyDisabled") {
    return { label: "Disabled", variant: "error" };
  }

  if (
    item.expiredReason === "UsageLimitExceeded" ||
    (item.usageLimit > 0 && item.usageCount >= item.usageLimit)
  ) {
    return { label: "Limit Exceeded", variant: "error" };
  }

  if (item.expiredReason === "TimeExpired" || (expiryDate && expiryDate < now)) {
    return { label: "Expired", variant: "error" };
  }

  if (item.isExpired) {
    return { label: "Expired", variant: "error" };
  }

  return { label: "Unknown", variant: "info" };
};

const getStatus = (item: MagicUrl) => {
  if (item.status) {
    switch (item.status) {
      case "Active":
        return { label: "Active", variant: "success" };
      case "Inactive":
      case "Disabled":
        return { label: item.status, variant: "error" };
      case "Expired":
        return { label: "Expired", variant: "error" };
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
