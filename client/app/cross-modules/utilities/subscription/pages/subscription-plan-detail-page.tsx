import { useMemo } from "react";
import { AlertCircle, Layers, Plus } from "lucide-react";
import { Link, useParams } from "react-router";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardTitle } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { ORGANIZATION_PAGE_SIZE } from "../constants/subscription.constants";
import { PlanSummaryCard, type PlanSummaryData } from "../components/plan-summary-card";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { useSubscriptionPlan } from "../hooks/use-subscription-plan";
import { formatEntitlementLimit, formatMeterAllowance, formatPrice } from "../utilities/subscription-format";

export const SubscriptionPlanDetailPage = () => {
  const { itemId, planId } = useParams();
  const basePath = `/app/${itemId ?? ""}/subscription/plans`;

  const { data: plan, error, isError, isLoading } = useSubscriptionPlan(planId);

  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: ORGANIZATION_PAGE_SIZE,
  });

  const planOrganizationId = plan?.organizationId;

  const organizationLabel = useMemo(() => {
    if (!planOrganizationId) {
      return "Tenant-wide";
    }

    const match = organizationsData?.organizations.find(
      (organization) => organization.itemId === planOrganizationId,
    );

    return match?.name ?? planOrganizationId;
  }, [organizationsData, planOrganizationId]);

  if (isLoading) {
    return (
      <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
        <Skeleton className="h-32 w-full rounded-2xl" />
        <Skeleton className="h-48 w-full rounded-xl" />
      </main>
    );
  }

  if (isError || !plan) {
    return (
      <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
        <SubscriptionPlanPageHeader
          title="Plan"
          description="This plan could not be loaded."
          backTo={basePath}
        />
        <Card className="flex min-h-56 flex-col items-center justify-center text-center">
          <span className="rounded-full bg-destructive/10 p-4 text-destructive">
            <AlertCircle className="h-7 w-7" />
          </span>
          <h3 className="mt-4 text-lg font-semibold">Plan not found</h3>
          <p className="mt-1 max-w-md text-sm text-muted-foreground">
            {error instanceof Error
              ? error.message
              : "It may belong to another organization, or no longer exists."}
          </p>
        </Card>
      </main>
    );
  }

  const summary: PlanSummaryData = {
    displayName: plan.displayName,
    code: plan.code,
    organizationLabel,
    trialDays: plan.trialDays,
    trialRequiresPaymentMethod: plan.trialRequiresPaymentMethod,
    quantityItems: plan.quantityItems.map((item) => ({
      itemKey: item.itemKey,
      unitLabel: item.unitLabel,
      defaultQuantity: item.defaultQuantity,
    })),
    meters: plan.meters,
    entitlements: plan.entitlements,
    prices: plan.prices,
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title={plan.displayName}
        description={plan.description || "No description provided."}
        backTo={basePath}
        actions={
          <Button asChild>
            <Link to={`${basePath}/${encodeURIComponent(plan.planId)}/prices/create`}>
              <Plus className="mr-2 h-4 w-4" />
              Add price
            </Link>
          </Button>
        }
      />

      <div className="grid items-start gap-5 xl:grid-cols-[1fr_22rem]">
        <div className="space-y-5">
          {plan.quantityItems.length > 0 && (
            <Section title="Quantity items">
              <div className="grid gap-3 sm:grid-cols-2">
                {plan.quantityItems.map((item) => (
                  <Card key={item.itemKey} className="rounded-lg">
                    <p className="font-medium">{item.itemKey}</p>
                    <p className="text-sm text-muted-foreground">
                      {item.defaultQuantity.toLocaleString()} {item.unitLabel}
                      {item.defaultQuantity === 1 ? "" : "s"} by default
                      {item.maxQuantity !== null &&
                        ` (max ${item.maxQuantity.toLocaleString()})`}
                    </p>
                  </Card>
                ))}
              </div>
            </Section>
          )}

          {plan.meters.length > 0 && (
            <Section title="Meters">
              <div className="grid gap-3 sm:grid-cols-2">
                {plan.meters.map((meter) => (
                  <Card key={meter.meterKey} className="rounded-lg">
                    <p className="font-medium">{meter.displayName}</p>
                    <p className="text-sm text-muted-foreground">
                      {formatMeterAllowance(meter)}
                    </p>
                    {meter.thresholdPercents.length > 0 && (
                      <p className="mt-2 text-xs text-muted-foreground">
                        Notifies at {meter.thresholdPercents.join("%, ")}% of allowance
                      </p>
                    )}
                    {meter.rateTables.length > 0 && (
                      <p className="mt-1 text-xs text-muted-foreground">
                        Tiered overage pricing configured for{" "}
                        {meter.rateTables.map((table) => table.currencyCode).join(", ")}
                      </p>
                    )}
                  </Card>
                ))}
              </div>
            </Section>
          )}

          {plan.entitlements.length > 0 && (
            <Section title="Entitlements">
              <div className="grid gap-3 sm:grid-cols-2">
                {plan.entitlements.map((entitlement) => (
                  <Card key={entitlement.key} className="rounded-lg">
                    <p className="font-medium">{entitlement.key}</p>
                    <p className="text-sm text-muted-foreground">
                      {formatEntitlementLimit(entitlement)}
                    </p>
                  </Card>
                ))}
              </div>
            </Section>
          )}

          <Section title="Prices">
            {plan.prices.length === 0 ? (
              <Card className="flex flex-col items-center gap-3 rounded-lg py-8 text-center">
                <Layers className="h-6 w-6 text-muted-foreground" />
                <p className="text-sm text-muted-foreground">
                  No price yet — subscribers cannot check out until one exists.
                </p>
                <Button asChild size="sm">
                  <Link to={`${basePath}/${encodeURIComponent(plan.planId)}/prices/create`}>
                    <Plus className="mr-2 h-4 w-4" />
                    Add price
                  </Link>
                </Button>
              </Card>
            ) : (
              <div className="grid gap-3 sm:grid-cols-2">
                {plan.prices.map((price) => (
                  <Card key={price.priceId} className="rounded-lg">
                    <p className="font-medium">{formatPrice(price)}</p>
                    <Badge variant="outline" className="mt-2 font-normal">
                      {price.currencyCode}
                    </Badge>
                  </Card>
                ))}
              </div>
            )}
          </Section>
        </div>

        <div className="xl:sticky xl:top-5">
          <PlanSummaryCard plan={summary} />
        </div>
      </div>
    </main>
  );
};

const Section = ({ title, children }: { title: string; children: React.ReactNode }) => (
  <div className="space-y-3">
    <CardTitle className="text-base">{title}</CardTitle>
    {children}
  </div>
);
