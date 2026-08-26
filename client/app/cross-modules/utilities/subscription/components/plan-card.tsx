import { ArrowRight, Gauge, Layers } from "lucide-react";
import { Link } from "react-router";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Card } from "@/components/ui-kits/card/card";
import type { SubscriptionPlan } from "../models/subscription-plan.model";
import { formatPrice } from "../utilities/subscription-format";
import { describeTrialDuration } from "../utilities/trial-duration-label";

export const PlanCard = ({
  plan,
  organizationLabel,
  detailPath,
}: {
  plan: SubscriptionPlan;
  organizationLabel: string;
  detailPath: string;
}) => {
  const cheapestPrice = plan.prices.reduce<SubscriptionPlan["prices"][number] | null>(
    (cheapest, price) =>
      cheapest === null || price.unitAmountMinor < cheapest.unitAmountMinor
        ? price
        : cheapest,
    null,
  );
  const trialLabel = describeTrialDuration(plan);

  return (
    <Link to={detailPath} className="block">
      <Card className="h-full rounded-xl transition hover:border-blocks-primary-300 hover:shadow-md">
        <div className="flex items-start justify-between gap-2">
          <div>
            <h3 className="font-semibold">{plan.displayName}</h3>
            <p className="text-xs text-muted-foreground">{plan.code}</p>
          </div>
          <ArrowRight className="h-4 w-4 shrink-0 text-muted-foreground" />
        </div>

        <div className="mt-3 flex flex-wrap gap-1.5">
          <Badge variant="outline" className="font-normal">
            {organizationLabel}
          </Badge>
          {trialLabel ? (
            <Badge variant="info" className="font-normal">
              {trialLabel}
            </Badge>
          ) : null}
          {plan.quantityItems.length > 0 && (
            <Badge variant="secondary" className="gap-1 font-normal">
              <Layers className="h-3 w-3" />
              {plan.quantityItems.length} quantity item
              {plan.quantityItems.length === 1 ? "" : "s"}
            </Badge>
          )}
          {plan.meters.length > 0 && (
            <Badge variant="secondary" className="gap-1 font-normal">
              <Gauge className="h-3 w-3" />
              {plan.meters.length} meter{plan.meters.length === 1 ? "" : "s"}
            </Badge>
          )}
        </div>

        <p className="mt-3 text-sm text-muted-foreground">
          {cheapestPrice
            ? `From ${formatPrice(cheapestPrice)}`
            : "No price configured yet"}
        </p>
      </Card>
    </Link>
  );
};
