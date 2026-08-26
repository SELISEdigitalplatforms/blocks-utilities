import { AlertCircle, CalendarClock, ExternalLink, History, Inbox } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { formatPrice } from "../../subscription/utilities/subscription-format";
import type {
  QuantityDiscountTier,
  SimulatedSubscription,
} from "../models/subscription-simulation.model";
import { SubscriptionStatusBadge } from "./subscription-status-badge";

const formatDate = (isoDate: string | null) =>
  isoDate ? new Date(isoDate).toLocaleString() : "—";

const describeTier = (tier: QuantityDiscountTier) => {
  const range =
    tier.maximumQuantity === null
      ? `${tier.minimumQuantity}+`
      : `${tier.minimumQuantity}\u2013${tier.maximumQuantity}`;

  return tier.discountBasisPoints > 0
    ? `${range} band \u00b7 ${Number((tier.discountBasisPoints / 100).toFixed(2))}% off`
    : `${range} band \u00b7 no discount`;
};

const CANCELABLE_STATUSES = new Set(["Incomplete", "Trialing", "Active", "PastDue"]);
// Incomplete has not paid yet, so it is not eligible to change plan — the doc has you continue
// or cancel that checkout instead.
const CHANGEABLE_STATUSES = new Set(["Trialing", "Active", "PastDue"]);

export const CurrentSubscriptionCard = ({
  subscription,
  isLoading,
  isError,
  error,
  scopeLabel,
  onRetry,
  onCancel,
  onChangePlan,
  onChangeQuantity,
  onCancelPendingQuantityChange,
  isCancelingPendingQuantityChange,
  onViewAuditTrail,
}: {
  subscription: SimulatedSubscription | null | undefined;
  isLoading: boolean;
  isError: boolean;
  error: unknown;
  scopeLabel: string;
  onRetry: () => void;
  onCancel: () => void;
  onChangePlan: () => void;
  onChangeQuantity: () => void;
  onCancelPendingQuantityChange: () => void;
  isCancelingPendingQuantityChange: boolean;
  onViewAuditTrail: () => void;
}) => {
  if (isLoading) {
    return <Skeleton className="h-28 w-full rounded-xl" />;
  }

  if (isError) {
    return (
      <Card className="flex flex-col items-start gap-2 rounded-xl border-destructive/30 bg-destructive/5 p-4">
        <div className="flex items-center gap-2 text-destructive">
          <AlertCircle className="h-4 w-4" />
          <span className="font-medium">Current subscription could not be loaded</span>
        </div>
        <p className="text-sm text-muted-foreground">
          {error instanceof Error ? error.message : "Try again in a moment."}
        </p>
        <Button size="sm" variant="outline" onClick={onRetry}>
          Try again
        </Button>
      </Card>
    );
  }

  if (!subscription) {
    return (
      <Card className="flex items-center gap-3 rounded-xl p-4 text-sm text-muted-foreground">
        <Inbox className="h-5 w-5 shrink-0" />
        <span>
          <strong className="text-foreground">{scopeLabel}</strong> has no subscription yet.
          Subscribe to a plan below to start one.
        </span>
      </Card>
    );
  }

  return (
    <Card className="rounded-xl p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <h3 className="font-semibold">{subscription.planName}</h3>
            <SubscriptionStatusBadge status={subscription.status} />
          </div>
          <p className="text-xs text-muted-foreground">
            {scopeLabel} · {subscription.planCode} ·{" "}
            {formatPrice({
              currencyCode: subscription.currencyCode,
              unitAmountMinor: subscription.unitAmountMinor,
              interval: subscription.interval,
              intervalCount: subscription.intervalCount,
              quantityItemKey: subscription.quantities[0]?.itemKey ?? null,
            })}
          </p>
        </div>

        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="ghost" onClick={onViewAuditTrail}>
            <History className="mr-2 h-3.5 w-3.5" />
            Audit trail
          </Button>
          {subscription.checkoutUrl && (
            <Button size="sm" asChild>
              <a href={subscription.checkoutUrl} target="_blank" rel="noreferrer">
                Continue checkout
                <ExternalLink className="ml-2 h-3.5 w-3.5" />
              </a>
            </Button>
          )}
          {CHANGEABLE_STATUSES.has(subscription.status) && subscription.quantities.length > 0 && (
            <Button size="sm" variant="outline" onClick={onChangeQuantity}>
              Change quantity
            </Button>
          )}
          {CHANGEABLE_STATUSES.has(subscription.status) && (
            <Button size="sm" variant="outline" onClick={onChangePlan}>
              Change plan
            </Button>
          )}
          {CANCELABLE_STATUSES.has(subscription.status) && (
            <Button size="sm" variant="destructive-outline" onClick={onCancel}>
              Cancel
            </Button>
          )}
        </div>
      </div>

      <div className="mt-3 flex flex-wrap gap-x-6 gap-y-1 text-xs text-muted-foreground">
        <span className="flex items-center gap-1">
          <CalendarClock className="h-3.5 w-3.5" />
          Current period ends {formatDate(subscription.currentPeriodEndUtc)}
        </span>
        {subscription.trialEndsAtUtc && (
          <span>Trial ends {formatDate(subscription.trialEndsAtUtc)}</span>
        )}
        {subscription.cancelAtPeriodEnd && (
          <span className="text-warning-800">Cancels at period end</span>
        )}
        {subscription.quantities.length > 0 && (
          <span>
            {subscription.quantities
              .map((quantity) => `${quantity.itemKey} × ${quantity.quantity}`)
              .join(", ")}
          </span>
        )}
        {/* The band decides what every one of those units costs, so it belongs next to them
            rather than only inside the change dialog. */}
        {subscription.currentTier && (
          <span>{describeTier(subscription.currentTier)}</span>
        )}
      </div>

      {/* A reduction already booked. Shown on the subscription itself, not only in the response to
          the request that made it: without this a reload shows the larger quantity with nothing to
          say a smaller one is coming, and no way to call it off. */}
      {subscription.pendingQuantityChange && (
        <div className="mt-3 flex flex-wrap items-center justify-between gap-2 rounded-md border border-warning-300 bg-warning-50 p-2.5 text-xs">
          <span className="flex items-center gap-1.5 text-warning-900">
            <CalendarClock className="h-3.5 w-3.5 shrink-0" />
            Reducing to{" "}
            {subscription.pendingQuantityChange.quantities
              .map((entry) => `${entry.quantity} ${entry.unitLabel ?? entry.itemKey}`)
              .join(", ")}{" "}
            on {formatDate(subscription.pendingQuantityChange.effectiveAtUtc)}
          </span>
          <Button
            size="sm"
            variant="outline"
            onClick={onCancelPendingQuantityChange}
            disabled={isCancelingPendingQuantityChange}
          >
            Keep current quantity
          </Button>
        </div>
      )}
    </Card>
  );
};
