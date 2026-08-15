import { CircleDollarSign, Gauge, Layers, ShieldCheck } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import {
  formatEntitlementLimit,
  formatMeterAllowance,
  formatPrice,
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
  }[];
  entitlements: { key: string; limitKind: string; limit: number | null; unitLabel: string | null }[];
  prices: {
    currencyCode: string;
    unitAmountMinor: number;
    interval: string;
    intervalCount: number;
    quantityItemKey: string | null;
  }[];
}

/**
 * The plan in plain language, the way it will actually read to a subscriber. Shared by the
 * builder's Review step and the plan detail page so the two never drift into describing the same
 * plan two different ways.
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
              ? `A card is required up front; nothing is charged until the ${plan.trialDays}-day trial ends.`
              : `Subscribers get full access for ${plan.trialDays} days with no card on file.`}
          </p>
        ) : null}

        {plan.quantityItems.length > 0 && (
          <div className="flex items-start gap-2 text-sm">
            <Layers className="mt-0.5 h-4 w-4 shrink-0 text-blocks-primary-600" />
            <div className="space-y-0.5">
              {plan.quantityItems.map((item) => (
                <p key={item.itemKey}>
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
              {plan.meters.map((meter) => (
                <p key={meter.meterKey}>
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
              {plan.entitlements.map((entitlement) => (
                <p key={entitlement.key}>
                  <span className="font-medium">{entitlement.key}:</span>{" "}
                  {formatEntitlementLimit(entitlement)}
                </p>
              ))}
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
