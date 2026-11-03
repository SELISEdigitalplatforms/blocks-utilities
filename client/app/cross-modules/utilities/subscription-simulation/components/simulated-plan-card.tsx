import { Gauge, Layers } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import {
  SUBSCRIBER_SCOPE,
  type SubscriptionPlan,
} from "../../subscription/models/subscription-plan.model";
import { formatPrice } from "../../subscription/utilities/subscription-format";
import { useT } from "@seliseblocks/genesis-os";

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
  const { t } = useT("utilities.subscription-simulation");
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
          {/* Said up front: subscribing to one does not become the organization's subscription —
              it adds places people are given, beside whatever the organization already holds. */}
          {plan.subscriberScope === SUBSCRIBER_SCOPE.User ? (
            <Badge variant="info" className="font-normal">
              {t("SIMULATED_PLAN_CARD.PER_PERSON")}
            </Badge>
          ) : null}
          {plan.trialDays ? (
            <Badge variant="info" className="font-normal">
              {t("SIMULATED_PLAN_CARD.DAY_TRIAL", { trialDays: plan.trialDays })}</Badge>
          ) : null}
          {plan.quantityItems.length > 0 && (
            <Badge variant="secondary" className="gap-1 font-normal">
              <Layers className="h-3 w-3" />
              {t("SIMULATED_PLAN_CARD.QUANTITY_ITEMS", { count: plan.quantityItems.length })}
            </Badge>
          )}
          {plan.meters.length > 0 && (
            <Badge variant="secondary" className="gap-1 font-normal">
              <Gauge className="h-3 w-3" />
              {t("SIMULATED_PLAN_CARD.METERS", { count: plan.meters.length })}
            </Badge>
          )}
        </div>

        <p className="mt-3 text-sm text-muted-foreground">
          {cheapestPrice ? t("SIMULATED_PLAN_CARD.FROM", { cheapestPrice: formatPrice(cheapestPrice) }) : t("SIMULATED_PLAN_CARD.NO_PRICE_CONFIGURED_YET")}
        </p>
      </div>

      <Button
        className="mt-4"
        disabled={!cheapestPrice || hasActiveSubscription}
        onClick={onSubscribe}
      >
        {hasActiveSubscription
          ? t("SIMULATED_PLAN_CARD.ALREADY_SUBSCRIBED")
          : cheapestPrice
            ? t("SIMULATED_PLAN_CARD.SUBSCRIBE")
            : t("SIMULATED_PLAN_CARD.NO_PRICE_TO_SUBSCRIBE_ON")}
      </Button>
    </Card>
  );
};
