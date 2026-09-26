import { useState } from "react";
import { AlertCircle, Gauge } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import type {
  PlanMeter,
  SubscriptionPlan,
} from "../../subscription/models/subscription-plan.model";
import { useCurrentUsage } from "../hooks/use-current-usage";
import { useMyUsage } from "../hooks/use-my-usage";
import { UsageMeterRow } from "./usage-meter-row";

type UsageView = "organization" | "mine";

/**
 * @param plan The organization's own plan, when it has a live subscription.
 * @param memberPlans Plans the organization has user-wise subscriptions to. What "mine" can draw on
 * beyond the organization's own — the server picks a place first for any meter one covers.
 */
export const UsageSection = ({
  plan,
  memberPlans = [],
  organizationId,
}: {
  plan: SubscriptionPlan | undefined;
  memberPlans?: SubscriptionPlan[];
  organizationId: string | undefined;
}) => {
  // Organization first when there is one, since that is what this section has always shown.
  const [chosenView, setView] = useState<UsageView>("organization");
  const hasBothViews = Boolean(plan) && memberPlans.length > 0;
  // Only a choice while both exist; otherwise the one there is.
  const view: UsageView = hasBothViews ? chosenView : plan ? "organization" : "mine";

  // The figures shown come from here, not from the entitlement snapshot: this is per meter and
  // exists whether or not an entitlement gates it. The per-consume entitlement check still runs
  // inside each row, right before acting, which is the point of the two-step flow. Only the read
  // on screen is enabled, so toggling does not fire both.
  const organizationRead = useCurrentUsage(organizationId, view === "organization");
  const myRead = useMyUsage(organizationId, view === "mine");
  const { data: usage, error, isError, isLoading, refetch } =
    view === "organization" ? organizationRead : myRead;

  const meters = metersFor(view, plan, memberPlans);

  return (
    <Card className="rounded-xl p-0">
      <div className="flex flex-col gap-3 border-b p-4 sm:flex-row sm:items-start sm:justify-between sm:p-5">
        <div>
          <h2 className="font-semibold">Usage</h2>
          <p className="text-xs text-muted-foreground">
            Each consume checks{" "}
            <code className="mx-1 rounded bg-muted px-1">GET /api/entitlements/{"{key}"}</code>{" "}
            first and decides from that answer, then records with{" "}
            <code className="mx-1 rounded bg-muted px-1">POST /api/subscription-usage</code>,
            which is the call that actually enforces. The figures below are read from{" "}
            {view === "organization" ? (
              <>
                <code className="mx-1 rounded bg-muted px-1">GET /api/subscription-usage/current</code>
                — the organization&apos;s subscription as a whole.
              </>
            ) : (
              <>
                <code className="mx-1 rounded bg-muted px-1">GET /api/subscription-usage/mine</code>
                — what you are the one spending: your place where you hold one, the
                organization&apos;s for everything else.
              </>
            )}
          </p>
        </div>

        {hasBothViews ? (
          <div className="flex shrink-0 gap-1 rounded-md border p-0.5" role="group" aria-label="Whose usage">
            {(["organization", "mine"] as const).map((option) => (
              <Button
                key={option}
                size="sm"
                variant={view === option ? "default" : "ghost"}
                aria-pressed={view === option}
                onClick={() => setView(option)}
              >
                {option === "organization" ? "Organization" : "Mine"}
              </Button>
            ))}
          </div>
        ) : null}
      </div>

      <div className="p-4 sm:p-5">
        {isLoading ? (
          <div className="space-y-3">
            <Skeleton className="h-14 w-full rounded-lg" />
            <Skeleton className="h-14 w-full rounded-lg" />
          </div>
        ) : isError ? (
          <div className="flex flex-col items-start gap-2">
            <div className="flex items-center gap-2 text-destructive">
              <AlertCircle className="h-4 w-4" />
              <span className="font-medium">Current usage could not be loaded</span>
            </div>
            <p className="text-sm text-muted-foreground">
              {error instanceof Error ? error.message : "Try again in a moment."}
            </p>
            <Button size="sm" variant="outline" onClick={() => refetch()}>
              Try again
            </Button>
          </div>
        ) : !meters.length ? (
          <div className="flex min-h-32 flex-col items-center justify-center text-center text-sm text-muted-foreground">
            <Gauge className="mb-2 h-6 w-6" />
            This plan has no metered usage to consume.
          </div>
        ) : (
          <div>
            {meters.map(({ meter, entitlementKey }) => (
              <UsageMeterRow
                key={meter.meterKey}
                meter={meter}
                entitlementKey={entitlementKey}
                usage={usage?.find((row) => row.meterKey === meter.meterKey)}
                organizationId={organizationId}
              />
            ))}
          </div>
        )}
      </div>
    </Card>
  );
};

/**
 * The meters a view can show, each with the entitlement that gates it — found through the plan's
 * own authored link (entitlement.meterKey), not by assuming the two keys match.
 *
 * "Mine" takes each meter from the first plan that has it, places before the organization's, which
 * is the order a recording resolves in — so the row a meter appears under is the plan its next
 * use is counted against.
 */
const metersFor = (
  view: UsageView,
  plan: SubscriptionPlan | undefined,
  memberPlans: SubscriptionPlan[],
): { meter: PlanMeter; entitlementKey: string | undefined }[] => {
  const sources = view === "organization" ? [plan] : [...memberPlans, plan];
  const found = new Map<string, { meter: PlanMeter; entitlementKey: string | undefined }>();

  for (const source of sources) {
    for (const meter of source?.meters ?? []) {
      if (!found.has(meter.meterKey)) {
        found.set(meter.meterKey, {
          meter,
          entitlementKey: source?.entitlements.find(
            (candidate) => candidate.meterKey === meter.meterKey,
          )?.key,
        });
      }
    }
  }

  return [...found.values()];
};
