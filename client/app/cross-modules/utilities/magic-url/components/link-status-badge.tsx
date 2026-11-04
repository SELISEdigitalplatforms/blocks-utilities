import React from "react";
import { useT } from "@seliseblocks/genesis-os";
import { Badge } from "@/components/ui-kits/badge/badge";

export interface LinkStatusBadgeProps {
  status: string;
}

export function LinkStatusBadge({ status }: LinkStatusBadgeProps) {
  const { t } = useT("utilities.magic-url");
  const getStatusLabel = (status: string): string => {
    const statusMap: Record<string, string> = {
      Active: t("LINK_STATUS_BADGE.ACTIVE"),
      Expired: t("LINK_STATUS_BADGE.EXPIRED"),
      Link_Disabled: t("common:DISABLED"),
      Link_Action_Limit_Exceeded: t("LINK_STATUS_BADGE.LIMIT_EXCEEDED"),
    };
    return statusMap[status] || status;
  };

  const getVariant = (status: string) => {
    const upperStatus = status.toUpperCase();
    if (upperStatus === "ACTIVE") {
      return "success";
    }
    if (
      upperStatus === "EXPIRED" ||
      upperStatus === "DISABLED" ||
      upperStatus === "INACTIVE" ||
      upperStatus === "LINK_ACTION_LIMIT_EXCEEDED"
    ) {
      return "error";
    }
    return "secondary";
  };

  const displayLabel = getStatusLabel(status);

  return (
    <Badge
      variant={getVariant(status)}
      className="whitespace-nowrap rounded-full"
    >
      {displayLabel}
    </Badge>
  );
}
