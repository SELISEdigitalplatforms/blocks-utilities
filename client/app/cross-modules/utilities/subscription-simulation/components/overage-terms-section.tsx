import { AlertTriangle, Ban, Calculator, Gauge } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { formatInterval } from "../../subscription/utilities/subscription-format";
import type { MeterTerms, SimulatedSubscription } from "../models/subscription-simulation.model";
import { EstimateUsageDialog } from "./estimate-usage-dialog";

/**
 * The statuses whose overage can actually be estimated. Mirrors the server's own
 * `LiveStatuses`/`GetLiveAsync` -- the overage preview endpoint resolves the subscription with
 * that same live lookup, so calling it for anything outside this set (Incomplete, Unpaid,
 * Canceled...) would only ever answer "no active subscription."
 */
const PREVIEWABLE_STATUSES = new Set(["Trialing", "Active", "PastDue"]);

/**
 * "every month" -> "per month", "every 3 months" -> "per 3 months". Reuses the cadence phrasing
 * every other price on this page already uses, rather than a second table of interval labels.
 */
const perCadence = (interval: string, intervalCount: number): string =>
  formatInterval(interval, intervalCount).replace(/^every /, "per ");

const describeAllowance = (
  meter: MeterTerms,
  usageInterval: string,
  usageIntervalCount: number,
): string => {
  const plural = meter.includedQuantity === 1 ? "" : "s";
  const included = `${meter.includedQuantity.toLocaleString()} ${meter.unitLabel}${plural} included`;

  if (meter.resetPolicy === "Never") {
    return `${included} for the life of the subscription.`;
  }

  if (meter.resetPolicy === "CarryForward") {
    const cap =
      meter.carryForwardCap != null
        ? ` (up to ${meter.carryForwardCap.toLocaleString()} rolling into the next)`
        : "";
    return `${included} ${perCadence(usageInterval, usageIntervalCount)}, with unused usage carried forward${cap}.`;
  }

  return `${included} ${perCadence(usageInterval, usageIntervalCount)}.`;
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
    const amount = `${pricing.currencyCode} ${tier.unitAmount} each`;

    if (tier.upToQuantity == null) {
      return index === 0 ? amount : `thereafter ${amount}`;
    }

    const span = tier.upToQuantity - previousBoundary;
    const label =
      index === 0
        ? `First ${span.toLocaleString()} additional ${unit}${span === 1 ? "" : "s"}`
        : `Next ${span.toLocaleString()} ${unit}${span === 1 ? "" : "s"}`;

    previousBoundary = tier.upToQuantity;

    return `${label}: ${amount}`;
  });

  return `${segments.join("; ")}.`;
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
  const pricingText = describeOveragePricing(meter);
  const isPriced = meter.overageAllowed && Boolean(pricingText);
  const isUnpriced = meter.overageAllowed && !pricingText;

  return (
    <div className="flex flex-col gap-2 border-b py-3 last:border-b-0 sm:flex-row sm:items-start sm:justify-between">
      <div className="min-w-0 space-y-1">
        <p className="text-sm font-medium">{meter.displayName}</p>
        <p className="text-xs text-muted-foreground">
          {describeAllowance(meter, usageInterval, usageIntervalCount)}{" "}
          {!meter.overageAllowed && (
            <span className="inline-flex items-center gap-1">
              <Ban className="h-3 w-3" />
              Additional usage is blocked.
            </span>
          )}
          {isPriced && pricingText}
        </p>
        {isUnpriced && (
          <p className="flex items-start gap-1.5 text-xs text-warning-800">
            <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            Additional usage is allowed, but no overage price is configured for the subscription
            currency.
          </p>
        )}
        {isPriced && !canEstimate && (
          <p className="text-xs text-muted-foreground">
            Estimating additional usage becomes available once the subscription is active.
          </p>
        )}
      </div>

      {isPriced && canEstimate && (
        <Button size="sm" variant="outline" onClick={onEstimate} className="shrink-0">
          <Calculator className="mr-2 h-3.5 w-3.5" />
          Estimate additional usage
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
          Overage terms
        </h2>
        <p className="text-xs text-muted-foreground">
          What this subscription actually bought for each meter, from{" "}
          <code className="mx-1 rounded bg-muted px-1">GET /api/subscriptions/current</code>
          — fixed at signup, unaffected by later edits to the plan catalogue.
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
