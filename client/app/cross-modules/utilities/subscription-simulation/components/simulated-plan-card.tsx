import { Gauge, Layers } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import { formatPrice } from "../../subscription/utilities/subscription-format";

export const SimulatedPlanCard = ({
  plan,
  organizationLabel,
  hasActiveSubscription,
  onSubscribe,
}: {
  plan: SubscriptionPlan;
  organizationLabel: string;
  hasActiveSubscription: boolean;
  onSubscribe: () => void;
}) => {
  const cheapestPrice = plan.prices.reduce<SubscriptionPlan["prices"][number] | null>(
    (cheapest, price) =>
      cheapest === null || price.unitAmountMinor < cheapest.unitAmountMinor
        ? price
        : cheapest,
    null,
  );

  return (
    <Card className="flex h-full flex-col justify-between rounded-xl">
      <div>
        <div className="flex items-start justify-between gap-2">
          <div>
            <h3 className="font-semibold">{plan.displayName}</h3>
            <p className="text-xs text-muted-foreground">{plan.code}</p>
          </div>
        </div>

        <div className="mt-3 flex flex-wrap gap-1.5">
          <Badge variant="outline" className="font-normal">
            {organizationLabel}
          </Badge>
          {plan.trialDays ? (
            <Badge variant="info" className="font-normal">
              {plan.trialDays}-day trial
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
          {cheapestPrice ? `From ${formatPrice(cheapestPrice)}` : "No price configured yet"}
        </p>
      </div>

      <Button
        className="mt-4"
        disabled={!cheapestPrice || hasActiveSubscription}
        onClick={onSubscribe}
      >
        {hasActiveSubscription
          ? "Already subscribed"
          : cheapestPrice
            ? "Subscribe"
            : "No price to subscribe on"}
      </Button>
    </Card>
  );
};
