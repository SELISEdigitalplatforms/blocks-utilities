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
import { useChangeSubscriptionPlan } from "../hooks/use-change-subscription-plan";
import { usePreviewPlanChange } from "../hooks/use-preview-plan-change";
import type {
  ChangeSubscriptionPlanRequest,
  SimulatedSubscription,
  SubscriptionPlanChangePreview,
  SubscriptionQuantity,
} from "../models/subscription-simulation.model";
import { labelPlanChange } from "../utilities/plan-change-label";

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
  const preview = usePreviewPlanChange();
  const apply = useChangeSubscriptionPlan();

  const [targetPlanId, setTargetPlanId] = useState(currentPlan?.planId ?? "");
  const [priceId, setPriceId] = useState("");
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const [quote, setQuote] = useState<SubscriptionPlanChangePreview | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [confirmationProfileGap, setConfirmationProfileGap] =
    useState<BillingProfileGap | null>(null);

  const busy = preview.isPending || apply.isPending;

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
        (plan?.quantityItems ?? []).map((item) => [item.itemKey, String(item.defaultQuantity)]),
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
      return { valid: false, error: "Choose a target plan and price." };
    }

    if (currencyMismatch) {
      return {
        valid: false,
        error: "This price is in a different currency. A currency change needs a cancel and a " +
          "fresh subscription, not a plan change.",
      };
    }

    const parsedQuantities: SubscriptionQuantity[] = [];
    for (const item of targetPlan.quantityItems) {
      const raw = quantities[item.itemKey] ?? "";
      const quantity = Number(raw);

      if (!raw || !Number.isFinite(quantity) || quantity < item.minQuantity) {
        return { valid: false, error: `${item.unitLabel} must be at least ${item.minQuantity}.` };
      }

      if (item.maxQuantity != null && quantity > item.maxQuantity) {
        return { valid: false, error: `${item.unitLabel} can be at most ${item.maxQuantity}.` };
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
        error instanceof Error ? error.message : "The plan change could not be previewed.",
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
        title: scheduledFor ? "Plan change scheduled" : `${moveLabel ?? "Plan changed"}`,
        description: scheduledFor
          ? `Moving to ${targetPlan!.displayName} on ${formatDate(scheduledFor)}. Nothing was charged.`
          : `Now on ${targetPlan!.displayName}.`,
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
              (error instanceof Error ? error.message : "The plan could not be changed."),
      );
      // The quote may no longer describe what a retry would charge.
      setQuote(null);
    }
  };

  const blocked = (quote?.blockers.length ?? 0) > 0;
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
          <DialogTitle>Change plan</DialogTitle>
          <DialogDescription>
            Sends <code>PUT /api/subscriptions/{subscription.subscriptionId}/plan</code> — the
            server follows the chosen price back to its plan and rebuilds billing from there.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="change-plan-target">Target plan</Label>
            <Select value={targetPlanId} onValueChange={selectTargetPlan}>
              <SelectTrigger id="change-plan-target">
                <SelectValue placeholder="Choose a plan" />
              </SelectTrigger>
              <SelectContent>
                {plans.map((plan) => (
                  <SelectItem key={plan.planId} value={plan.planId}>
                    {plan.displayName}
                    {plan.planId === currentPlan?.planId ? " (current)" : ""}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {targetPlan && (
            <div className="space-y-1.5">
              <Label htmlFor="change-plan-price">Target price</Label>
              <Select value={priceId} onValueChange={selectPrice}>
                <SelectTrigger id="change-plan-price">
                  <SelectValue placeholder="Choose a price" />
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
              <span className="text-xs text-muted-foreground">This is labelled as</span>
              <Badge variant="info" className="font-normal">
                {moveLabel}
              </Badge>
              {currencyMismatch && (
                <Badge variant="error" className="font-normal">
                  Currency mismatch
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
            The change takes effect immediately and starts a full target billing period — an
            upgrade may charge immediately, a downgrade becomes credit toward future renewals.
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
                      ? "Charged now"
                      : "Applies now, nothing charged"
                    : `Effective ${formatDate(quote.effectiveAtUtc)}`}
                </p>
                <Badge variant={quote.timing === "Immediate" ? "default" : "secondary"}>
                  {quote.timing === "Immediate"
                    ? formatMoney(quote.chargeMinor, quote.currencyCode)
                    : "Nothing due today"}
                </Badge>
              </div>

              {quote.timing === "NextRenewal" ? (
                <p className="text-xs text-warning-900" data-testid="plan-change-scheduled-note">
                  You keep your current plan, at the price you are already paying, until{" "}
                  {formatDate(quote.effectiveAtUtc)}. Nothing is charged or refunded today, and you
                  can cancel this before it takes effect.
                </p>
              ) : null}

              <Row
                label="Next full period"
                value={formatMoney(quote.nextRenewalAmountMinor, quote.currencyCode)}
              />
              {quote.settlement.netSettlementMinor !== 0 ? (
                <Row
                  label="Net settlement"
                  value={formatMoney(quote.settlement.netSettlementMinor, quote.currencyCode)}
                />
              ) : null}
              {quote.settlement.creditConsumedMinor > 0 ? (
                <Row
                  label="Paid from your credit"
                  value={formatMoney(quote.settlement.creditConsumedMinor, quote.currencyCode)}
                />
              ) : null}

              <p className="text-xs text-muted-foreground">
                Priced against the current clock — a plan change is never frozen ahead of
                confirming, so re-preview if this has sat open for a while.
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
            Cancel
          </Button>
          <Button variant="outline" onClick={runPreview} disabled={busy}>
            {preview.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            Preview
          </Button>
          <Button onClick={submit} disabled={busy || quote === null || blocked}>
            {apply.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            Confirm change
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
    case "subscription_pending_quantity_change_exists":
      return "A quantity change is already scheduled for the end of this period. Cancel it on the " +
        "subscription first — only one change can be waiting at a time.";
    case "subscription_quantity_change_in_flight":
      return "A quantity change is still being settled. Try again once it finishes.";
    case "subscription_initial_annual_period_prepaid":
      return "This subscription has already paid for its first year, so it cannot move to another " +
        "plan until that year begins. A downgrade can still be scheduled now.";
    case "subscription_initial_annual_period_unpaid":
      return "This subscription's first year has not been charged yet, so it cannot move to " +
        "another plan until it is. A downgrade can still be scheduled now.";
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
