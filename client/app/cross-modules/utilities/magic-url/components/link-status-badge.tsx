import React from "react";
import { Badge } from "@/components/ui-kits/badge/badge";

export interface LinkStatusBadgeProps {
  status: string;
}

export function LinkStatusBadge({ status }: LinkStatusBadgeProps) {
  const getStatusLabel = (status: string): string => {
    const statusMap: Record<string, string> = {
      Active: "Active",
      Expired: "Expired",
      Link_Disabled: "Disabled",
      Link_Action_Limit_Exceeded: "Limit Exceeded",
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
      variant={getVariant(status) as any}
      className="whitespace-nowrap rounded-full"
    >
      {displayLabel}
    </Badge>
  );
}
