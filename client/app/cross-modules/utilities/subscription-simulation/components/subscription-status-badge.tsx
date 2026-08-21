import { Badge, type BadgeProps } from "@/components/ui-kits/badge/badge";
import type { SubscriptionStatus } from "../models/subscription-simulation.model";

/** Mirrors the lifecycle in the integration guide: only three states actually grant anything. */
const STATUS_VARIANT: Record<SubscriptionStatus, BadgeProps["variant"]> = {
  Trialing: "info",
  Active: "success",
  PastDue: "info",
  Incomplete: "secondary",
  IncompleteExpired: "error",
  Unpaid: "error",
  Canceled: "secondary",
};

export const SubscriptionStatusBadge = ({ status }: { status: SubscriptionStatus }) => (
  <Badge variant={STATUS_VARIANT[status]} className="font-normal">
    {status}
  </Badge>
);
