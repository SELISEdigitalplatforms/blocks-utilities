import { AlertTriangle, Loader2 } from "lucide-react";
import { useMemo, useState } from "react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { toast } from "@/hooks/use-toast";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import { formatMoney, formatPrice } from "../../subscription/utilities/subscription-format";
import {
  billingProfileGapOf,
  subscriptionApiFailure,
  type BillingProfileGap,
} from "../../subscription/utilities/subscription-api-failure";
import { BillingProfileIncompleteNotice } from "./billing-profile-incomplete-notice";
import { useWithdrawCancellation } from "../hooks/use-cancel-subscription";
import { useChangeSubscriptionPlan } from "../hooks/use-change-subscription-plan";
import { usePreviewPlanChange } from "../hooks/use-preview-plan-change";
import type {
  ChangeSubscriptionPlanRequest,
  SimulatedSubscription,
  SubscriptionPlanChangePreview,
  SubscriptionQuantity,
} from "../models/subscription-simulation.model";
import { labelPlanChange } from "../utilities/plan-change-label";
import { useT, tStatic } from "@seliseblocks/genesis-os";

/**
 * Moving a subscription to another plan or price.
 *
 * Nothing is confirmed from this screen without a preview first, and any edit that would change
 * the price — the target plan, the target price, a quantity — discards the quote. Unlike the
 * subscribe dialog's, this quote is never frozen server-side: re-preview if more than a moment
 * has passed before confirming.
 */
export const ChangePlanDialog = ({
  subscription,
  currentPlan,
  plans,
  organizationId,
  open,
  onOpenChange,
}: {
  subscription: SimulatedSubscription;
  currentPlan: SubscriptionPlan | undefined;
  plans: SubscriptionPlan[];
  organizationId: string | undefined;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) => {
  const { t } = useT("utilities.subscription-simulation");
  const preview = usePreviewPlanChange();
  const apply = useChangeSubscriptionPlan();
  const withdrawCancellation = useWithdrawCancellation();

  const [targetPlanId, setTargetPlanId] = useState(currentPlan?.planId ?? "");
  const [priceId, setPriceId] = useState("");
  const [quantities, setQuantities] = useState<Record<string, string>>(() =>
    Object.fromEntries(subscription.quantities.map((held) => [held.itemKey, String(held.quantity)])),
  );
  const [quote, setQuote] = useState<SubscriptionPlanChangePreview | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [confirmationProfileGap, setConfirmationProfileGap] =
    useState<BillingProfileGap | null>(null);

  const busy = preview.isPending || apply.isPending || withdrawCancellation.isPending;

  const targetPlan = useMemo(
    () => plans.find((plan) => plan.planId === targetPlanId),
    [plans, targetPlanId],
  );

  const selectedPrice = useMemo(
    () => targetPlan?.prices.find((price) => price.priceId === priceId),
    [targetPlan, priceId],
  );

  const moveLabel = targetPlan ? labelPlanChange(currentPlan, targetPlan) : null;
  const currencyMismatch = Boolean(
    selectedPrice && selectedPrice.currencyCode !== subscription.currencyCode,
  );

  const selectTargetPlan = (planId: string) => {
    setTargetPlanId(planId);
    const plan = plans.find((candidate) => candidate.planId === planId);
    setPriceId(plan?.prices[0]?.priceId ?? "");
    setQuantities(
      Object.fromEntries(
        // What the subscription holds now where the item carries over — the plan's default would
        // quietly drop places a change of plan was never meant to touch.
        (plan?.quantityItems ?? []).map((item) => [
          item.itemKey,
          String(
            subscription.quantities.find((held) => held.itemKey === item.itemKey)?.quantity ??
              item.defaultQuantity,
          ),
        ]),
      ),
    );
    setFormError(null);
    setQuote(null);
    setConfirmationProfileGap(null);
  };

  const selectPrice = (value: string) => {
    setPriceId(value);
    setQuote(null);
    setConfirmationProfileGap(null);
  };

  const editQuantity = (itemKey: string, value: string) => {
    setQuantities((current) => ({ ...current, [itemKey]: value }));
    setQuote(null);
    setConfirmationProfileGap(null);
  };

  const requested = ():
    | { valid: true; request: ChangeSubscriptionPlanRequest }
    | { valid: false; error: string } => {
    if (!targetPlan || !priceId) {
      return { valid: false, error: t("CHANGE_PLAN_DIALOG.CHOOSE_A_TARGET_PLAN_AND") };
    }

    if (currencyMismatch) {
      return {
        valid: false,
        error: t("CHANGE_PLAN_DIALOG.THIS_PRICE_IS_IN_A"),
      };
    }

    const parsedQuantities: SubscriptionQuantity[] = [];
    for (const item of targetPlan.quantityItems) {
      const raw = quantities[item.itemKey] ?? "";
      const quantity = Number(raw);

      if (!raw || !Number.isFinite(quantity) || quantity < item.minQuantity) {
        return { valid: false, error: t("CHANGE_PLAN_DIALOG.MUST_BE_AT_LEAST", { unitLabel: item.unitLabel, minQuantity: item.minQuantity }) };
      }

      if (item.maxQuantity != null && quantity > item.maxQuantity) {
        return { valid: false, error: t("CHANGE_PLAN_DIALOG.CAN_BE_AT_MOST", { unitLabel: item.unitLabel, maxQuantity: item.maxQuantity }) };
      }

      parsedQuantities.push({ itemKey: item.itemKey, quantity });
    }

    return {
      valid: true,
      request: {
        planCode: targetPlan.code,
        priceId,
        quantities: parsedQuantities,
        organizationId,
      },
    };
  };

  const runPreview = async () => {
    const parsed = requested();

    if (!parsed.valid) {
      setFormError(parsed.error);
      return;
    }

    setFormError(null);
    setConfirmationProfileGap(null);

    try {
      setQuote(
        await preview.mutateAsync({
          subscriptionId: subscription.subscriptionId,
          request: parsed.request,
        }),
      );
    } catch (error) {
      setFormError(
        error instanceof Error ? error.message : t("CHANGE_PLAN_DIALOG.THE_PLAN_CHANGE_COULD_NOT"),
      );
      setQuote(null);
    }
  };

  const submit = async () => {
    const parsed = requested();

    if (!parsed.valid) {
      setFormError(parsed.error);
      return;
    }

    setFormError(null);

    try {
      await apply.mutateAsync({
        subscriptionId: subscription.subscriptionId,
        request: parsed.request,
      });

      // A scheduled change has not happened yet, so it must not be announced as though it had.
      const scheduledFor = quote?.timing === "NextRenewal" ? quote.effectiveAtUtc : null;

      toast({
        variant: "success",
        title: scheduledFor ? t("CHANGE_PLAN_DIALOG.PLAN_CHANGE_SCHEDULED") : `${moveLabel ?? "Plan changed"}`,
        description: scheduledFor
          ? t("CHANGE_PLAN_DIALOG.MOVING_TO_ON_NOTHING_WAS", { displayName: targetPlan!.displayName, scheduledFor: formatDate(scheduledFor) })
          : t("CHANGE_PLAN_DIALOG.NOW_ON", { displayName: targetPlan!.displayName }),
      });

      onOpenChange(false);
    } catch (error) {
      // A plan change moves money too, so it is refused for the same incomplete profile — and the
      // fix is the same page. See the subscribe dialog.
      const failure = subscriptionApiFailure(error);
      const gap = billingProfileGapOf(failure);

      setConfirmationProfileGap(gap);
      setFormError(
        gap
          ? null
          : describePlanChangeRefusal(failure?.code) ||
              failure?.message ||
              (error instanceof Error ? error.message : t("CHANGE_PLAN_DIALOG.THE_PLAN_COULD_NOT_BE")),
      );
      // The quote may no longer describe what a retry would charge.
      setQuote(null);
    }
  };

  const undoCancellation = async () => {
    try {
      await withdrawCancellation.mutateAsync({
        subscriptionId: subscription.subscriptionId,
        organizationId,
      });
      toast({
        variant: "success",
        title: t("CHANGE_PLAN_DIALOG.CANCELLATION_UNDONE"),
        description: t("CHANGE_PLAN_DIALOG.THIS_SUBSCRIPTION_KEEPS_RENEWING"),
      });
    } catch (error) {
      toast({
        variant: "destructive",
        title: t("CHANGE_PLAN_DIALOG.COULD_NOT_UNDO_THE_CANCELLATION"),
        description:
          error instanceof Error ? error.message : t("CHANGE_PLAN_DIALOG.THE_CANCELLATION_COULD_NOT_BE"),
      });
    }
  };

  const blocked = (quote?.blockers.length ?? 0) > 0 || subscription.cancelAtPeriodEnd;
  const previewProfileGap = quote?.blockers
    .map((blocker) =>
      billingProfileGapOf({
        code: blocker.code,
        fields: blocker.fields ?? {},
      }),
    )
    .find((gap): gap is BillingProfileGap => gap !== null);
  const profileGap = confirmationProfileGap ?? previewProfileGap ?? null;

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!busy) {
          onOpenChange(next);
        }
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("CHANGE_PLAN_DIALOG.CHANGE_PLAN")}</DialogTitle>
          <DialogDescription>
            {t("CHANGE_PLAN_DIALOG.SENDS_SERVER_FOLLOWS_PRICE", { endpoint: `PUT /api/subscriptions/${subscription.subscriptionId}/plan` })}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          {subscription.cancelAtPeriodEnd && (
            <div
              className="flex items-start justify-between gap-2 rounded-md border border-warning-300 bg-warning-50 p-2.5 text-xs text-warning-900"
              data-testid="change-plan-cancellation-notice"
            >
              <span className="flex items-start gap-1.5">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                {t("CHANGE_PLAN_DIALOG.THIS_SUBSCRIPTION_IS_SCHEDULED_TO_2", { currentPeriodEndUtc: formatDate(subscription.currentPeriodEndUtc) })}</span>
              <Button
                size="sm"
                variant="outline"
                onClick={undoCancellation}
                disabled={busy}
              >
                {t("CHANGE_PLAN_DIALOG.UNDO_CANCELLATION")}
              </Button>
            </div>
          )}

          <div className="space-y-1.5">
            <Label htmlFor="change-plan-target">{t("CHANGE_PLAN_DIALOG.TARGET_PLAN")}</Label>
            <Select value={targetPlanId} onValueChange={selectTargetPlan}>
              <SelectTrigger id="change-plan-target">
                <SelectValue placeholder={t("CHANGE_PLAN_DIALOG.CHOOSE_A_PLAN")} />
              </SelectTrigger>
              <SelectContent>
                {plans.map((plan) => (
                  <SelectItem key={plan.planId} value={plan.planId}>
                    {plan.displayName}
                    {plan.planId === currentPlan?.planId ? ` ${t("CHANGE_PLAN_DIALOG.CURRENT_SUFFIX")}` : ""}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {targetPlan && (
            <div className="space-y-1.5">
              <Label htmlFor="change-plan-price">{t("CHANGE_PLAN_DIALOG.TARGET_PRICE")}</Label>
              <Select value={priceId} onValueChange={selectPrice}>
                <SelectTrigger id="change-plan-price">
                  <SelectValue placeholder={t("CHANGE_PLAN_DIALOG.CHOOSE_A_PRICE")} />
                </SelectTrigger>
                <SelectContent>
                  {targetPlan.prices.map((price) => (
                    <SelectItem key={price.priceId} value={price.priceId}>
                      {price.displayPriceNote ?? formatPrice(price)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}

          {moveLabel && (
            <div className="flex items-center gap-2">
              <span className="text-xs text-muted-foreground">{t("CHANGE_PLAN_DIALOG.THIS_IS_LABELLED_AS")}</span>
              <Badge variant="info" className="font-normal">
                {moveLabel}
              </Badge>
              {currencyMismatch && (
                <Badge variant="error" className="font-normal">
                  {t("CHANGE_PLAN_DIALOG.CURRENCY_MISMATCH")}
                </Badge>
              )}
            </div>
          )}

          {targetPlan?.quantityItems.map((item) => (
            <div className="space-y-1.5" key={item.itemKey}>
              <Label htmlFor={`change-quantity-${item.itemKey}`}>{item.unitLabel}</Label>
              <Input
                id={`change-quantity-${item.itemKey}`}
                type="number"
                min={item.minQuantity}
                max={item.maxQuantity ?? undefined}
                value={quantities[item.itemKey] ?? ""}
                onChange={(event) => editQuantity(item.itemKey, event.target.value)}
              />
            </div>
          ))}

          <p className="text-xs text-muted-foreground">
            {t("CHANGE_PLAN_DIALOG.PREVIEW_TO_SEE_WHETHER_THIS")}
          </p>

          {profileGap && (
            <BillingProfileIncompleteNotice gap={profileGap} organizationId={organizationId} />
          )}

          {quote ? (
            <div className="space-y-2 rounded-md border p-3 text-sm" data-testid="plan-change-quote">
              {/* Timing first, because it changes what every figure below means. A scheduled
                  change takes nothing today however large its settlement comes out. */}
              <div className="flex items-center justify-between">
                <p className="font-medium">
                  {quote.timing === "Immediate"
                    ? quote.chargeMinor > 0
                      ? t("CHANGE_PLAN_DIALOG.CHARGED_NOW")
                      : t("CHANGE_PLAN_DIALOG.APPLIES_NOW_NOTHING_CHARGED")
                    : t("CHANGE_PLAN_DIALOG.EFFECTIVE", { effectiveAtUtc: formatDate(quote.effectiveAtUtc) })}
                </p>
                <Badge variant={quote.timing === "Immediate" ? "default" : "secondary"}>
                  {quote.timing === "Immediate"
                    ? formatMoney(quote.chargeMinor, quote.currencyCode)
                    : t("CHANGE_PLAN_DIALOG.NOTHING_DUE_TODAY")}
                </Badge>
              </div>

              {quote.timing === "NextRenewal" ? (
                <p className="text-xs text-warning-900" data-testid="plan-change-scheduled-note">
                  {t("CHANGE_PLAN_DIALOG.YOU_KEEP_YOUR_CURRENT_PLAN", { effectiveAtUtc: formatDate(quote.effectiveAtUtc) })}</p>
              ) : null}

              <Row
                label={t("CHANGE_PLAN_DIALOG.NEXT_FULL_PERIOD")}
                value={formatMoney(quote.nextRenewalAmountMinor, quote.currencyCode)}
              />
              {quote.settlement.annual ? (
                // A prepaid opening stub's compatible upgrade settles two things at once: the
                // days left on the stub, and the paid year repriced onto the new terms. Shown
                // apart because that is what the invoice will say too — a single combined figure
                // would read as one oversized plan-change fee with no explanation for the size.
                // Each is its own raw delta, before credit — credit is spent once, below, against
                // the combined total, never against either side alone.
                <>
                  <Row
                    label={t("CHANGE_PLAN_DIALOG.OPENING_STUB_ADJUSTMENT")}
                    value={formatMoney(
                      quote.settlement.target.proratedValueMinor -
                        quote.settlement.outgoing.proratedValueMinor,
                      quote.currencyCode,
                    )}
                  />
                  <Row
                    label={t("CHANGE_PLAN_DIALOG.PREPAID_ANNUAL_PERIOD_ADJUSTMENT")}
                    value={formatMoney(
                      quote.settlement.annual.netSettlementMinor,
                      quote.currencyCode,
                    )}
                  />
                </>
              ) : null}
              {/* Only for a change that settles today: a scheduled one moves no money, and a
                  negative figure beside "nothing is refunded" read as a refund. */}
              {quote.timing === "Immediate" && quote.settlement.netSettlementMinor !== 0 ? (
                <Row
                  label={t("CHANGE_PLAN_DIALOG.NET_SETTLEMENT")}
                  value={formatMoney(quote.settlement.netSettlementMinor, quote.currencyCode)}
                />
              ) : null}
              {quote.settlement.creditConsumedMinor > 0 ? (
                <Row
                  label={t("CHANGE_PLAN_DIALOG.PAID_FROM_YOUR_CREDIT")}
                  value={formatMoney(quote.settlement.creditConsumedMinor, quote.currencyCode)}
                />
              ) : null}

              <p className="text-xs text-muted-foreground">
                {t("CHANGE_PLAN_DIALOG.PRICED_AGAINST_THE_CURRENT_CLOCK")}
              </p>

              {quote.blockers
                .filter((blocker) => blocker.code !== "subscription_billing_profile_incomplete")
                .map((blocker) => (
                <div
                  key={blocker.code}
                  className="flex items-start gap-2 rounded-md border border-warning-300 bg-warning-50 p-2 text-warning-900"
                >
                  <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                  <p>{blocker.message}</p>
                </div>
                ))}
            </div>
          ) : null}

          {formError && <p className="text-sm text-destructive">{formError}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={busy}>
            {t("common:CANCEL")}
          </Button>
          <Button
            variant="outline"
            onClick={runPreview}
            disabled={busy || subscription.cancelAtPeriodEnd}
          >
            {preview.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            {t("CHANGE_PLAN_DIALOG.PREVIEW")}
          </Button>
          <Button onClick={submit} disabled={busy || quote === null || blocked}>
            {apply.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            {t("CHANGE_PLAN_DIALOG.CONFIRM_CHANGE")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};

const formatDate = (isoDate: string) => new Date(isoDate).toLocaleString();

/**
 * The refusals worth saying something better than the server's own wording about.
 *
 * Each of these is a `409` the subscriber can actually resolve, and the resolution is not
 * obvious from the code alone — so the message names the thing to go and do. Anything else falls
 * back to the server's message, which is already written for a person.
 */
const describePlanChangeRefusal = (code: string | undefined): string | null => {
  switch (code) {
    case "subscription_cancellation_scheduled":
      return tStatic("utilities.subscription-simulation", "CHANGE_PLAN_DIALOG.THIS_SUBSCRIPTION_IS_SCHEDULED_TO");
    case "subscription_pending_quantity_change_exists":
      return tStatic("utilities.subscription-simulation", "CHANGE_PLAN_DIALOG.A_QUANTITY_CHANGE_IS_ALREADY");
    case "subscription_quantity_change_in_flight":
      return tStatic("utilities.subscription-simulation", "CHANGE_PLAN_DIALOG.A_QUANTITY_CHANGE_IS_STILL");
    // subscription_initial_annual_period_prepaid is retired: a prepaid opening stub no longer
    // refuses a compatible plan change outright — it settles the stub and the paid year together
    // instead (the quote above already reflects that combined charge). Only an unpaid stub still
    // refuses one outright, below.
    case "subscription_initial_annual_period_unpaid":
      return tStatic("utilities.subscription-simulation", "CHANGE_PLAN_DIALOG.THIS_SUBSCRIPTION_S_FIRST_YEAR");
    default:
      return null;
  }
};

const Row = ({ label, value }: { label: string; value: string }) => (
  <div className="flex items-baseline justify-between gap-4">
    <span className="text-muted-foreground">{label}</span>
    <span className="text-right font-medium">{value}</span>
  </div>
);
