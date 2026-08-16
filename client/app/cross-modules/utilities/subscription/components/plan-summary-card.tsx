import { CircleDollarSign, Gauge, Hourglass, Layers, ShieldCheck } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { describeEntitlementMeterMismatch } from "../utilities/plan-consistency";
import {
  formatEntitlementLimit,
  formatMeterAllowance,
  formatPrice,
  formatTrialAllowance,
} from "../utilities/subscription-format";

export interface PlanSummaryData {
  displayName: string;
  code: string;
  organizationLabel: string;
  trialDays: number | null;
  trialRequiresPaymentMethod: boolean;
  quantityItems: { itemKey: string; unitLabel: string; defaultQuantity: number }[];
  meters: {
    meterKey: string;
    displayName: string;
    unitLabel: string;
    includedQuantity: number;
    overageAllowed: boolean;
    /** Drives whether overage is described as billed or given away. */
    rateTables?: { currencyCode: string }[];
  }[];
  entitlements: {
    key: string;
    limitKind: string;
    limit: number | null;
    unitLabel: string | null;
    /** The meter this draws down, so the summary can say when the two disagree. */
    meterKey: string | null;
  }[];
  prices: {
    currencyCode: string;
    unitAmountMinor: number;
    interval: string;
    intervalCount: number;
    quantityItemKey: string | null;
  }[];
  /** A meter's allowance for the length of the trial, replacing the plan's own. */
  trialGrants: { meterKey: string; includedQuantity: number }[];
}

/**
 * The plan in plain language, the way it will actually read to a subscriber. Shared by the
 * builder's Review step and the plan detail page so the two never drift into describing the same
 * plan two different ways.
 */
/*
 * Every list below is keyed by index on purpose. The obvious keys — meterKey, itemKey,
 * entitlement.key — are fields the author is still typing, so they are empty on a freshly added
 * row and collide across rows the moment there are two. Duplicate keys leave React's
 * reconciliation of the list undefined, which stranded a half-filled row in this card while the
 * Review step, mounted later from the same data, rendered correctly. These rows hold no state,
 * never reorder, and are pure projections of the array, so the index is both stable and unique.
 */
export const PlanSummaryCard = ({ plan }: { plan: PlanSummaryData }) => {
  const hasPricing = plan.quantityItems.length > 0 || plan.prices.length > 0;
  const hasUsage = plan.meters.length > 0;
  const hasEntitlements = plan.entitlements.length > 0;

  return (
    <Card className="rounded-xl">
      <CardHeader className="gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <CardTitle>{plan.displayName || "Untitled plan"}</CardTitle>
          {plan.trialDays ? (
            <Badge variant="info">{plan.trialDays}-day trial</Badge>
          ) : null}
        </div>
        <p className="text-xs text-muted-foreground">
          {plan.code || "no-code-yet"} · {plan.organizationLabel}
        </p>
      </CardHeader>

      <CardContent className="space-y-4">
        {plan.prices.length > 0 && (
          <div className="flex flex-wrap gap-2">
            {plan.prices.map((price, index) => (
              <Badge key={index} variant="secondary" className="font-normal">
                {formatPrice(price)}
              </Badge>
            ))}
          </div>
        )}

        {plan.trialDays ? (
          <p className="text-sm text-muted-foreground">
            {plan.trialRequiresPaymentMethod
              ? `Charged at signup; the ${plan.trialDays}-day trial governs the allowances, not the price.`
              : `Free for ${plan.trialDays} days, then the first charge is taken.`}
          </p>
        ) : null}

        {/* Only worth showing where there is something to measure: a trial on a plan with no
            meters changes nothing but when the first charge falls. */}
        {plan.trialDays && hasUsage ? (
          <div className="flex items-start gap-2 text-sm">
            <Hourglass className="mt-0.5 h-4 w-4 shrink-0 text-blocks-primary-600" />
            <div className="space-y-0.5">
              <p className="font-medium">During the {plan.trialDays}-day trial</p>
              {plan.meters.map((meter, index) => (
                <p key={index}>
                  {meter.displayName}:{" "}
                  {formatTrialAllowance(
                    meter,
                    plan.trialGrants.find((grant) => grant.meterKey === meter.meterKey),
                  )}
                </p>
              ))}
            </div>
          </div>
        ) : null}

        {plan.quantityItems.length > 0 && (
          <div className="flex items-start gap-2 text-sm">
            <Layers className="mt-0.5 h-4 w-4 shrink-0 text-blocks-primary-600" />
            <div className="space-y-0.5">
              {plan.quantityItems.map((item, index) => (
                <p key={index}>
                  {item.defaultQuantity.toLocaleString()} {item.unitLabel}
                  {item.defaultQuantity === 1 ? "" : "s"} included by default
                </p>
              ))}
            </div>
          </div>
        )}

        {hasUsage && (
          <div className="flex items-start gap-2 text-sm">
            <Gauge className="mt-0.5 h-4 w-4 shrink-0 text-blocks-primary-600" />
            <div className="space-y-0.5">
              {plan.meters.map((meter, index) => (
                <p key={index}>
                  <span className="font-medium">{meter.displayName}:</span>{" "}
                  {formatMeterAllowance(meter)}
                </p>
              ))}
            </div>
          </div>
        )}

        {hasEntitlements && (
          <div className="flex items-start gap-2 text-sm">
            <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-blocks-primary-600" />
            <div className="space-y-0.5">
              {plan.entitlements.map((entitlement, index) => {
                const mismatch = describeEntitlementMeterMismatch(entitlement, plan.meters);

                return (
                  <div key={index}>
                    <p>
                      <span className="font-medium">{entitlement.key}:</span>{" "}
                      {formatEntitlementLimit(entitlement)}
                    </p>
                    {mismatch && (
                      <p className="text-xs text-warning-700">{mismatch}</p>
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        )}

        {!hasPricing && !hasUsage && !hasEntitlements && (
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <CircleDollarSign className="h-4 w-4 shrink-0" />
            Nothing configured yet — this plan grants nothing and charges nothing.
          </div>
        )}
      </CardContent>
    </Card>
  );
};
