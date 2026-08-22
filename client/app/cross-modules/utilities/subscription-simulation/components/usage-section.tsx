import { AlertCircle, Gauge } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import { useEntitlements } from "../hooks/use-entitlements";
import { UsageMeterRow } from "./usage-meter-row";

export const UsageSection = ({
  plan,
  organizationId,
}: {
  plan: SubscriptionPlan | undefined;
  organizationId: string | undefined;
}) => {
  const {
    data: entitlements,
    error,
    isError,
    isLoading,
    refetch,
  } = useEntitlements(organizationId);

  return (
    <Card className="rounded-xl p-0">
      <div className="border-b p-4 sm:p-5">
        <h2 className="font-semibold">Usage</h2>
        <p className="text-xs text-muted-foreground">
          Each consume checks{" "}
          <code className="mx-1 rounded bg-muted px-1">GET /api/entitlements/{"{key}"}</code>{" "}
          first and decides from that answer, then records with{" "}
          <code className="mx-1 rounded bg-muted px-1">POST /api/subscription-usage</code>,
          which is the call that actually enforces.
        </p>
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
              <span className="font-medium">Entitlements could not be loaded</span>
            </div>
            <p className="text-sm text-muted-foreground">
              {error instanceof Error ? error.message : "Try again in a moment."}
            </p>
            <Button size="sm" variant="outline" onClick={() => refetch()}>
              Try again
            </Button>
          </div>
        ) : !plan || !plan.meters.length ? (
          <div className="flex min-h-32 flex-col items-center justify-center text-center text-sm text-muted-foreground">
            <Gauge className="mb-2 h-6 w-6" />
            This plan has no metered usage to consume.
          </div>
        ) : (
          <div>
            {plan.meters.map((meter) => {
              // The entitlement that gates this meter, found through the plan's own authored
              // link (entitlement.meterKey), not by assuming the two keys match.
              const entitlementKey = plan.entitlements.find(
                (candidate) => candidate.meterKey === meter.meterKey,
              )?.key;

              return (
                <UsageMeterRow
                  key={meter.meterKey}
                  meter={meter}
                  entitlementKey={entitlementKey}
                  initialDecision={entitlements?.entitlements.find(
                    (decision) => decision.key === entitlementKey,
                  )}
                  organizationId={organizationId}
                />
              );
            })}
          </div>
        )}
      </div>
    </Card>
  );
};
