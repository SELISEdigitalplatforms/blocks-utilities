import { Badge, type BadgeProps } from "@/components/ui-kits/badge/badge";

/**
 * `outcome` is a free-form string owned by the server, not a closed enum the client validates —
 * worker-raised events can introduce a new one without this needing to change. Anything not
 * recognized falls back to a neutral badge rather than guessing.
 */
const OUTCOME_VARIANT: Record<string, BadgeProps["variant"]> = {
  Succeeded: "success",
  Rejected: "error",
  Failed: "error",
};

export const AuditOutcomeBadge = ({ outcome }: { outcome: string }) => (
  <Badge variant={OUTCOME_VARIANT[outcome] ?? "secondary"} className="font-normal">
    {outcome}
  </Badge>
);
