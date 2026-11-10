import { AlertCircle, CalendarClock, CreditCard, ExternalLink, History, Inbox } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { formatPrice } from "../../subscription/utilities/subscription-format";
import { describePendingMove } from "../utilities/plan-change-label";
import {
  isOfflineSubscription,
  type QuantityDiscountTier,
  type SimulatedSubscription,
} from "../models/subscription-simulation.model";
import { describeOfflinePricing } from "../utilities/offline-pricing";
import { BilledByInvoiceBadge } from "./offline-pricing-fields";
import { SubscriptionStatusBadge } from "./subscription-status-badge";
import { useT } from "@seliseblocks/genesis-os";

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
  onWithdrawCancellation,
  isWithdrawingCancellation,
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
  onWithdrawCancellation: () => void;
  isWithdrawingCancellation: boolean;
  onViewAuditTrail: () => void;
  /** Opens a card-collection session. Never a payment -- see the button labels below. */
  onAddPaymentMethod: () => void;
  isStartingPaymentMethodSetup: boolean;
}) => {
  const { t } = useT("utilities.subscription-simulation");
  if (isLoading) {
    return <Skeleton className="h-28 w-full rounded-xl" />;
  }

  if (isError) {
    return (
      <Card className="flex flex-col items-start gap-2 rounded-xl border-destructive/30 bg-destructive/5 p-4">
        <div className="flex items-center gap-2 text-destructive">
          <AlertCircle className="h-4 w-4" />
          <span className="font-medium">{t("CURRENT_SUBSCRIPTION_CARD.CURRENT_SUBSCRIPTION_COULD_NOT_BE")}</span>
        </div>
        <p className="text-sm text-muted-foreground">
          {error instanceof Error ? error.message : t("CURRENT_SUBSCRIPTION_CARD.TRY_AGAIN_IN_A_MOMENT")}
        </p>
        <Button size="sm" variant="outline" onClick={onRetry}>
          {t("CURRENT_SUBSCRIPTION_CARD.TRY_AGAIN")}
        </Button>
      </Card>
    );
  }

  if (!subscription) {
    return (
      <Card className="flex items-center gap-3 rounded-xl p-4 text-sm text-muted-foreground">
        <Inbox className="h-5 w-5 shrink-0" />
        <span>
          <strong className="text-foreground">{scopeLabel}</strong> {t("CURRENT_SUBSCRIPTION_CARD.HAS_NO_SUBSCRIPTION_YET_SUBSCRIBE")}
        </span>
      </Card>
    );
  }

  // Paid by bank transfer (spec 006): never charged and takes no card, so not one card prompt below
  // applies -- the server answers 409 to each of them anyway.
  const offline = isOfflineSubscription(subscription);

  // A session already open -- from any of the three cases below -- always wins over starting a
  // new one, so a subscriber part-way through Stripe sees where to finish it rather than a button
  // that would open a second, competing session.
  const pendingSetupUrl = offline ? null : (subscription.pendingCheckout?.checkoutUrl ?? null);

  // Card-free trial that has not added one yet. hasPaymentMethod is read from the account, not
  // guessed from the status: a card-required trial reaching Trialing already collected one, and a
  // card-free trial may have added one voluntarily, so status alone cannot say whether this is
  // still owed.
  const canAddPaymentMethod =
    !offline &&
    !pendingSetupUrl &&
    subscription.status === "Trialing" &&
    subscription.hasPaymentMethod === false;

  // Lost paid access for want of a card, and this is the one thing that gets it back.
  const canRecover = !offline && !pendingSetupUrl && subscription.status === "Unpaid";

  // A card on file that the subscriber wants to swap -- expiring, or cancelled by the issuer. The
  // same session as "Add payment method"; the card it stores replaces the current default.
  const canChangePaymentMethod =
    !offline &&
    !pendingSetupUrl &&
    CHANGEABLE_STATUSES.has(subscription.status) &&
    subscription.hasPaymentMethod === true;

  return (
    <Card className="rounded-xl p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <h3 className="font-semibold">{subscription.planName}</h3>
            <SubscriptionStatusBadge status={subscription.status} />
            {offline && <BilledByInvoiceBadge />}
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
            {t("CURRENT_SUBSCRIPTION_CARD.AUDIT_TRAIL")}
          </Button>
          {!offline && subscription.checkoutUrl && (
            <Button size="sm" asChild>
              <a href={subscription.checkoutUrl} target="_blank" rel="noreferrer">
                {t("CURRENT_SUBSCRIPTION_CARD.CONTINUE_CHECKOUT")}
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
                {t("CURRENT_SUBSCRIPTION_CARD.COMPLETE_CARD_SETUP")}
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
              {t("CURRENT_SUBSCRIPTION_CARD.ADD_PAYMENT_METHOD")}
            </Button>
          )}
          {canChangePaymentMethod && (
            <Button
              size="sm"
              variant="outline"
              onClick={onAddPaymentMethod}
              disabled={isStartingPaymentMethodSetup}
            >
              <CreditCard className="mr-2 h-3.5 w-3.5" />
              {t("CURRENT_SUBSCRIPTION_CARD.CHANGE_CARD")}
            </Button>
          )}
          {canRecover && (
            <Button size="sm" onClick={onAddPaymentMethod} disabled={isStartingPaymentMethodSetup}>
              <CreditCard className="mr-2 h-3.5 w-3.5" />
              {t("CURRENT_SUBSCRIPTION_CARD.ADD_CARD_AND_CONTINUE_SUBSCRIPTION")}
            </Button>
          )}
          {CHANGEABLE_STATUSES.has(subscription.status) && subscription.quantities.length > 0 && (
            <Button size="sm" variant="outline" onClick={onChangeQuantity}>
              {t("CURRENT_SUBSCRIPTION_CARD.CHANGE_QUANTITY")}
            </Button>
          )}
          {CHANGEABLE_STATUSES.has(subscription.status) && (
            <Button size="sm" variant="outline" onClick={onChangePlan}>
              {t("CURRENT_SUBSCRIPTION_CARD.CHANGE_PLAN")}
            </Button>
          )}
          {CANCELABLE_STATUSES.has(subscription.status) && (
            <Button size="sm" variant="destructive-outline" onClick={onCancel}>
              {t("common:CANCEL")}
            </Button>
          )}
        </div>
      </div>

      <div className="mt-3 flex flex-wrap gap-x-6 gap-y-1 text-xs text-muted-foreground">
        <span className="flex items-center gap-1">
          <CalendarClock className="h-3.5 w-3.5" />
          {t("CURRENT_SUBSCRIPTION_CARD.CURRENT_PERIOD_ENDS", { currentPeriodEndUtc: formatDate(subscription.currentPeriodEndUtc) })}
        </span>
        {offline && (
          <span data-testid="offline-pricing">
            {describeOfflinePricing(t, subscription.offlinePricingMode, subscription.contractAmountMinor, subscription.currencyCode)}
            {subscription.pendingOfflinePricingMode &&
              ` · ${t("OFFLINE_PRICING.FROM_NEXT_RENEWAL", {
                pricing: describeOfflinePricing(t, subscription.pendingOfflinePricingMode, subscription.pendingContractAmountMinor, subscription.currencyCode),
              })}`}
          </span>
        )}
        {subscription.trialEndsAtUtc && (
          <span>{t("CURRENT_SUBSCRIPTION_CARD.TRIAL_ENDS", { trialEndsAtUtc: formatDate(subscription.trialEndsAtUtc) })}</span>
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

      {/* A cancellation already scheduled. The subscription stays Active and keeps granting until
          the period ends, so this is the one place that says so and offers the one clear way
          back: undo it. Change plan is refused server-side while this is showing -- see the
          dialog's own notice. */}
      {subscription.cancelAtPeriodEnd && (
        <div
          className="mt-3 flex flex-wrap items-center justify-between gap-2 rounded-md border border-warning-300 bg-warning-50 p-2.5 text-xs"
          data-testid="scheduled-cancellation"
        >
          <span className="flex items-center gap-1.5 text-warning-900">
            <CalendarClock className="h-3.5 w-3.5 shrink-0" />
            {t("CURRENT_SUBSCRIPTION_CARD.CANCELLATION_SCHEDULED_FOR_YOU", { currentPeriodEndUtc: formatDate(subscription.currentPeriodEndUtc), planName: subscription.planName })}</span>
          <Button
            size="sm"
            variant="outline"
            onClick={onWithdrawCancellation}
            disabled={isWithdrawingCancellation}
          >
            {t("CURRENT_SUBSCRIPTION_CARD.KEEP_SUBSCRIPTION")}
          </Button>
        </div>
      )}

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
            {t("CURRENT_SUBSCRIPTION_CARD.MOVING_TO_ON_NOTHING_IS", { subscription: describePendingMove(subscription, subscription.pendingPlanChange), effectiveAtUtc: formatDate(subscription.pendingPlanChange.effectiveAtUtc), planName: subscription.planName })}</span>
          <Button
            size="sm"
            variant="outline"
            onClick={onCancelPendingPlanChange}
            disabled={isCancelingPendingPlanChange}
          >
            {t("CURRENT_SUBSCRIPTION_CARD.KEEP_CURRENT_PLAN")}
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
            {t("CURRENT_SUBSCRIPTION_CARD.REDUCING_TO_ON", { quantities: subscription.pendingQuantityChange.quantities
              .map((entry) => `${entry.quantity} ${entry.unitLabel ?? entry.itemKey}`)
              .join(", "), effectiveAtUtc: formatDate(subscription.pendingQuantityChange.effectiveAtUtc) })}
          </span>
          <Button
            size="sm"
            variant="outline"
            onClick={onCancelPendingQuantityChange}
            disabled={isCancelingPendingQuantityChange}
          >
            {t("CURRENT_SUBSCRIPTION_CARD.KEEP_CURRENT_QUANTITY")}
          </Button>
        </div>
      )}
    </Card>
  );
};
