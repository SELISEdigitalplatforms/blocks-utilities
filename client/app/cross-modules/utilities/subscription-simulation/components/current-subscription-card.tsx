import { AlertCircle, CalendarClock, CreditCard, ExternalLink, History, Inbox } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
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

// Debugging aid, not customer-facing marketing: an operator comparing two subscriptions that
// charge through different providers has no other way to see which is which from this screen.
const PROVIDER_LABELS: Record<string, string> = {
  STRIPE: "Stripe",
  "ADYEN-ONLINE": "Adyen",
};
const formatProviderName = (providerName: string | null) =>
  providerName ? (PROVIDER_LABELS[providerName] ?? providerName) : null;

const describeTier = (tier: QuantityDiscountTier) => {
  const range =
    tier.maximumQuantity === null
      ? `${tier.minimumQuantity}+`
      : `${tier.minimumQuantity}\u2013${tier.maximumQuantity}`;

  return tier.discountBasisPoints > 0
    ? `${range} band \u00b7 ${Number((tier.discountBasisPoints / 100).toFixed(2))}% off`
    : `${range} band \u00b7 no discount`;
};

// Unpaid included since #360 made it visible here at all -- before then it was never returned by
// GetCurrentAsync, so this set only had to name the statuses that were ever reachable on screen.
// The server has always accepted cancelling Unpaid (anything short of Canceled/IncompleteExpired);
// a subscriber offered nothing but "recover" here had no way to walk away instead.
const CANCELABLE_STATUSES = new Set(["Incomplete", "Trialing", "Active", "PastDue", "Unpaid"]);
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
  onCancelPendingPlanChange,
  isCancelingPendingPlanChange,
  onViewAuditTrail,
  onAddPaymentMethod,
  isStartingPaymentMethodSetup,
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
  onCancelPendingPlanChange: () => void;
  isCancelingPendingPlanChange: boolean;
  onViewAuditTrail: () => void;
  /** Opens a card-collection session. Never a payment -- see the button labels below. */
  onAddPaymentMethod: () => void;
  isStartingPaymentMethodSetup: boolean;
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

  // A session already open -- from any of the three cases below -- always wins over starting a
  // new one, so a subscriber part-way through Stripe sees where to finish it rather than a button
  // that would open a second, competing session.
  const pendingSetupUrl = subscription.pendingCheckout?.checkoutUrl ?? null;

  // Card-free trial that has not added one yet. hasPaymentMethod is read from the account, not
  // guessed from the status: a card-required trial reaching Trialing already collected one, and a
  // card-free trial may have added one voluntarily, so status alone cannot say whether this is
  // still owed.
  const canAddPaymentMethod =
    !pendingSetupUrl &&
    subscription.status === "Trialing" &&
    subscription.hasPaymentMethod === false;

  // Lost paid access for want of a card, and this is the one thing that gets it back.
  const canRecover = !pendingSetupUrl && subscription.status === "Unpaid";

  return (
    <Card className="rounded-xl p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <h3 className="font-semibold">{subscription.planName}</h3>
            <SubscriptionStatusBadge status={subscription.status} />
            {formatProviderName(subscription.providerName) && (
              <Badge variant="secondary" className="font-normal">
                {formatProviderName(subscription.providerName)}
              </Badge>
            )}
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
          {/*
            A card session, never a payment -- distinct from "Continue checkout" above, which is
            money. Three different moments land on the same button: an incomplete card-required
            trial's own signup session resuming, and a session either of the two calls below just
            opened.
          */}
          {pendingSetupUrl && (
            <Button size="sm" asChild>
              <a href={pendingSetupUrl} target="_blank" rel="noreferrer">
                <CreditCard className="mr-2 h-3.5 w-3.5" />
                Complete card setup
                <ExternalLink className="ml-2 h-3.5 w-3.5" />
              </a>
            </Button>
          )}
          {canAddPaymentMethod && (
            <Button
              size="sm"
              variant="outline"
              onClick={onAddPaymentMethod}
              disabled={isStartingPaymentMethodSetup}
            >
              <CreditCard className="mr-2 h-3.5 w-3.5" />
              Add payment method
            </Button>
          )}
          {canRecover && (
            <Button size="sm" onClick={onAddPaymentMethod} disabled={isStartingPaymentMethodSetup}>
              <CreditCard className="mr-2 h-3.5 w-3.5" />
              Add card and continue subscription
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

      {/* A plan change already booked, shown for the same reason the reduction below is: without
          it a reload shows the current plan with nothing to say a different one is coming, and no
          way to call it off. The subscriber keeps this plan, at this price, until the date shown. */}
      {subscription.pendingPlanChange && (
        <div
          className="mt-3 flex flex-wrap items-center justify-between gap-2 rounded-md border border-warning-300 bg-warning-50 p-2.5 text-xs"
          data-testid="pending-plan-change"
        >
          <span className="flex items-center gap-1.5 text-warning-900">
            <CalendarClock className="h-3.5 w-3.5 shrink-0" />
            Moving to {subscription.pendingPlanChange.targetPlanName} on{" "}
            {formatDate(subscription.pendingPlanChange.effectiveAtUtc)} — nothing is charged until
            then, and you keep {subscription.planName} until it does.
          </span>
          <Button
            size="sm"
            variant="outline"
            onClick={onCancelPendingPlanChange}
            disabled={isCancelingPendingPlanChange}
          >
            Keep current plan
          </Button>
        </div>
      )}

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
