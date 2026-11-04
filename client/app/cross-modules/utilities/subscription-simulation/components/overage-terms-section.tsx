import { AlertTriangle, Ban, Calculator, Gauge } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { formatInterval } from "../../subscription/utilities/subscription-format";
import type { MeterTerms, SimulatedSubscription } from "../models/subscription-simulation.model";
import { EstimateUsageDialog } from "./estimate-usage-dialog";
import { tStatic, useT } from "@seliseblocks/genesis-os";

const UTILITIES_SUBSCRIPTION_SIMULATION_MODULE = "utilities.subscription-simulation";

/**
 * The statuses whose overage can actually be estimated. Mirrors the server's own
 * `LiveStatuses`/`GetLiveAsync` -- the overage preview endpoint resolves the subscription with
 * that same live lookup, so calling it for anything outside this set (Incomplete, Unpaid,
 * Canceled...) would only ever answer "no active subscription."
 */
const PREVIEWABLE_STATUSES = new Set(["Trialing", "Active", "PastDue"]);

const describeAllowance = (
  meter: MeterTerms,
  usageInterval: string,
  usageIntervalCount: number,
): string => {
  const included = tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "OVERAGE_TERMS_SECTION.INCLUDED", {
    count: meter.includedQuantity,
    quantity: meter.includedQuantity.toLocaleString(),
    unit: meter.unitLabel,
  });

  if (meter.resetPolicy === "Never") {
    return tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "OVERAGE_TERMS_SECTION.ALLOWANCE_LIFETIME", { included });
  }

  const cadence = formatInterval(usageInterval, usageIntervalCount);

  if (meter.resetPolicy === "CarryForward") {
    return meter.carryForwardCap == null
      ? tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "OVERAGE_TERMS_SECTION.ALLOWANCE_CARRY_FORWARD", { included, cadence })
      : tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "OVERAGE_TERMS_SECTION.ALLOWANCE_CARRY_FORWARD_CAP", { included, cadence, cap: meter.carryForwardCap.toLocaleString() });
  }

  return tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "OVERAGE_TERMS_SECTION.ALLOWANCE_PERIODIC", { included, cadence });
};

/**
 * A trial's grant replaces the plan's allowance while the trial runs, so both are shown: the
 * grant is what usage is measured against now, the allowance is what the subscription moves to.
 */
const describeTrialAwareAllowance = (
  meter: MeterTerms,
  usageInterval: string,
  usageIntervalCount: number,
): string => {
  const allowance = describeAllowance(meter, usageInterval, usageIntervalCount);

  if (meter.trialIncludedQuantity == null) {
    return allowance;
  }

  return tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "OVERAGE_TERMS_SECTION.TRIAL_ALLOWANCE", {
    count: meter.trialIncludedQuantity,
    quantity: meter.trialIncludedQuantity.toLocaleString(),
    unit: meter.unitLabel,
    allowance,
  });
};

/**
 * "First 100 additional screenings: CHF 1.00 each; thereafter CHF 0.80 each." -- one segment per
 * graduated band, phrased from the bands' own boundaries rather than a fixed template, since a
 * meter may define anywhere from one flat rate to several.
 */
const describeOveragePricing = (meter: MeterTerms): string => {
  const pricing = meter.overagePricing;

  if (!pricing || pricing.tiers.length === 0) {
    return "";
  }

  const unit = meter.unitLabel || "unit";
  let previousBoundary = 0;

  const segments = pricing.tiers.map((tier, index) => {
    const amount = tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "OVERAGE_TERMS_SECTION.TIER_AMOUNT", { currency: pricing.currencyCode, amount: tier.unitAmount });

    if (tier.upToQuantity == null) {
      return index === 0 ? amount : tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "OVERAGE_TERMS_SECTION.THEREAFTER", { amount });
    }

    const span = tier.upToQuantity - previousBoundary;
    const label =
      index === 0
        ? tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "OVERAGE_TERMS_SECTION.FIRST_ADDITIONAL", { count: span, span: span.toLocaleString(), unit })
        : tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "OVERAGE_TERMS_SECTION.NEXT_UNITS", { count: span, span: span.toLocaleString(), unit });

    previousBoundary = tier.upToQuantity;

    return tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "OVERAGE_TERMS_SECTION.TIER_SEGMENT", { label, amount });
  });

  return tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "OVERAGE_TERMS_SECTION.PRICING_LIST", { segments: segments.join("; ") });
};

const MeterTermsRow = ({
  meter,
  usageInterval,
  usageIntervalCount,
  canEstimate,
  onEstimate,
}: {
  meter: MeterTerms;
  usageInterval: string;
  usageIntervalCount: number;
  canEstimate: boolean;
  onEstimate: () => void;
}) => {
  const { t } = useT(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE);
  const pricingText = describeOveragePricing(meter);
  const isPriced = meter.overageAllowed && Boolean(pricingText);
  const isUnpriced = meter.overageAllowed && !pricingText;

  return (
    <div className="flex flex-col gap-2 border-b py-3 last:border-b-0 sm:flex-row sm:items-start sm:justify-between">
      <div className="min-w-0 space-y-1">
        <p className="text-sm font-medium">{meter.displayName}</p>
        <p className="text-xs text-muted-foreground">
          {describeTrialAwareAllowance(meter, usageInterval, usageIntervalCount)}{" "}
          {!meter.overageAllowed && (
            <span className="inline-flex items-center gap-1">
              <Ban className="h-3 w-3" />
              {t("OVERAGE_TERMS_SECTION.ADDITIONAL_USAGE_IS_BLOCKED")}
            </span>
          )}
          {isPriced && pricingText}
        </p>
        {isUnpriced && (
          <p className="flex items-start gap-1.5 text-xs text-warning-800">
            <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            {t("OVERAGE_TERMS_SECTION.ADDITIONAL_USAGE_IS_ALLOWED_BUT")}
          </p>
        )}
        {isPriced && !canEstimate && (
          <p className="text-xs text-muted-foreground">
            {t("OVERAGE_TERMS_SECTION.ESTIMATING_ADDITIONAL_USAGE")}
          </p>
        )}
      </div>

      {isPriced && canEstimate && (
        <Button size="sm" variant="outline" onClick={onEstimate} className="shrink-0">
          <Calculator className="mr-2 h-3.5 w-3.5" />
          {t("OVERAGE_TERMS_SECTION.ESTIMATE_ADDITIONAL_USAGE")}
        </Button>
      )}
    </div>
  );
};

export const OverageTermsSection = ({
  subscription,
  organizationId,
}: {
  subscription: SimulatedSubscription | null | undefined;
  organizationId: string | undefined;
}) => {
  const { t } = useT(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE);
  const [estimating, setEstimating] = useState<MeterTerms | null>(null);

  if (!subscription || subscription.meters.length === 0) {
    return null;
  }

  const canEstimate = PREVIEWABLE_STATUSES.has(subscription.status);

  return (
    <Card className="rounded-xl p-0">
      <div className="border-b p-4 sm:p-5">
        <h2 className="flex items-center gap-2 font-semibold">
          <Gauge className="h-4 w-4" />
          {t("OVERAGE_TERMS_SECTION.OVERAGE_TERMS")}
        </h2>
        <p className="text-xs text-muted-foreground">
          {t("OVERAGE_TERMS_SECTION.WHAT_THIS_SUBSCRIPTION_BOUGHT", { endpoint: "GET /api/subscriptions/current" })}
        </p>
      </div>

      <div className="p-4 sm:p-5">
        {subscription.meters.map((meter) => (
          <MeterTermsRow
            key={meter.meterKey}
            meter={meter}
            usageInterval={subscription.usageInterval}
            usageIntervalCount={subscription.usageIntervalCount}
            canEstimate={canEstimate}
            onEstimate={() => setEstimating(meter)}
          />
        ))}
      </div>

      {estimating && (
        <EstimateUsageDialog
          meter={estimating}
          organizationId={organizationId}
          open={Boolean(estimating)}
          onOpenChange={(open) => {
            if (!open) {
              setEstimating(null);
            }
          }}
        />
      )}
    </Card>
  );
};
