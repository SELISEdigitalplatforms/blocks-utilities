import { AlertTriangle, CalendarClock, Loader2 } from "lucide-react";
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
import { toast } from "@/hooks/use-toast";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import { formatMoney } from "../../subscription/utilities/subscription-format";
import { useChangeQuantity, usePreviewQuantityChange } from "../hooks/use-quantity-change";
import type {
  QuantityChangeQuote,
  QuantityDiscountTier,
  SimulatedSubscription,
} from "../models/subscription-simulation.model";
import { SubscriptionOperationError } from "../services/subscription-simulation.service";
import { useT, tStatic } from "@seliseblocks/genesis-os";

/**
 * Changing how many units a subscription has bought, without changing what it is on.
 *
 * Quantity in, band out: the subscriber names a quantity and the server decides which volume band
 * that falls in. Letting a client name the band would be letting it name a price the plan may not
 * agree to.
 *
 * Nothing is charged from this screen without a preview first, and editing a quantity discards the
 * quote it produced — a confirmation that can be sent after its figures stopped applying is a
 * confirmation of numbers the subscriber never saw.
 */
export const ChangeQuantityDialog = ({
  subscription,
  currentPlan,
  organizationId,
  open,
  onOpenChange,
  onRefresh,
}: {
  subscription: SimulatedSubscription;
  currentPlan: SubscriptionPlan | undefined;
  /** The organization being acted on, when the console is acting on one. */
  organizationId?: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onRefresh: () => void;
}) => {
  const { t } = useT("utilities.subscription-simulation");
  const preview = usePreviewQuantityChange();
  const apply = useChangeQuantity();

  // Seeded once, from the subscription as it read when this opened. The dialog is mounted per
  // opening, so there is nothing to re-synchronize later — and a quote can never outlive the
  // quantities it was calculated from.
  const [targets, setTargets] = useState<Record<string, string>>(() =>
    Object.fromEntries(
      subscription.quantities.map((held) => [held.itemKey, String(held.quantity)]),
    ),
  );
  const [quote, setQuote] = useState<QuantityChangeQuote | null>(null);
  const [failure, setFailure] = useState<{ code: string; message: string } | null>(null);

  const items = useMemo(
    () =>
      subscription.quantities.map((held) => {
        const defined = currentPlan?.quantityItems.find(
          (candidate) => candidate.itemKey === held.itemKey,
        );

        // The subscription's own quantities carry the bounds of the plan snapshot it is priced
        // against, so they win over the live plan, which may have been edited since signup. Read
        // as a pair off minQuantity: a null maxQuantity from the server means no ceiling, not a
        // missing answer to look up elsewhere.
        const bounds =
          held.minQuantity === undefined
            ? { min: defined?.minQuantity ?? 1, max: defined?.maxQuantity ?? null }
            : { min: held.minQuantity, max: held.maxQuantity ?? null };

        return {
          itemKey: held.itemKey,
          unitLabel: held.unitLabel ?? defined?.unitLabel ?? held.itemKey,
          held: held.quantity,
          minQuantity: bounds.min,
          maxQuantity: bounds.max,
        };
      }),
    [subscription.quantities, currentPlan],
  );

  const busy = preview.isPending || apply.isPending;

  const edit = (itemKey: string, value: string) => {
    setTargets((current) => ({ ...current, [itemKey]: value }));
    // The quote described the quantities as they were a keystroke ago.
    setQuote(null);
    setFailure(null);
  };

  const requested = ():
    | { valid: true; quantities: { itemKey: string; quantity: number }[] }
    | { valid: false; error: string } => {
    const quantities: { itemKey: string; quantity: number }[] = [];

    for (const item of items) {
      const raw = targets[item.itemKey] ?? "";
      const quantity = Number(raw);

      if (!raw.trim() || !Number.isInteger(quantity)) {
        return { valid: false, error: t("CHANGE_QUANTITY_DIALOG.ENTER_A_WHOLE_NUMBER_OF", { unitLabel: item.unitLabel }) };
      }

      if (quantity < item.minQuantity) {
        return {
          valid: false,
          error: t("CHANGE_QUANTITY_DIALOG.THIS_PLAN_NEEDS_AT_LEAST", { count: item.minQuantity, unitLabel: item.unitLabel }),
        } as const;
      }

      if (item.maxQuantity !== null && quantity > item.maxQuantity) {
        return {
          valid: false,
          error: t("CHANGE_QUANTITY_DIALOG.THIS_PLAN_ALLOWS_AT_MOST", { count: item.maxQuantity, unitLabel: item.unitLabel }),
        } as const;
      }

      quantities.push({ itemKey: item.itemKey, quantity });
    }

    if (quantities.every((entry) => entry.quantity === items.find((item) => item.itemKey === entry.itemKey)?.held)) {
      return { valid: false, error: t("CHANGE_QUANTITY_DIALOG.THAT_IS_ALREADY_THE_QUANTITY") };
    }

    return { valid: true, quantities };
  };

  const run = async (mode: "preview" | "apply") => {
    const parsed = requested();

    if (!parsed.valid) {
      setFailure({ code: "local_validation", message: parsed.error });

      return;
    }

    setFailure(null);

    // The same body for the preview and the apply, scope included: a quote resolved against one
    // organization and applied against another is not the same operation.
    const request = {
      version: subscription.version,
      quantities: parsed.quantities,
      organizationId,
    };

    try {
      if (mode === "preview") {
        setQuote(
          await preview.mutateAsync({
            subscriptionId: subscription.subscriptionId,
            request,
          }),
        );

        return;
      }

      const applied = await apply.mutateAsync({
        subscriptionId: subscription.subscriptionId,
        request,
      });

      toast({
        variant: "success",
        title: applied.timing === "Immediate" ? t("CHANGE_QUANTITY_DIALOG.QUANTITY_UPDATED") : t("CHANGE_QUANTITY_DIALOG.CHANGE_SCHEDULED"),
        description:
          applied.timing === "Immediate"
            ? describeQuantities(applied)
            : t("CHANGE_QUANTITY_DIALOG.FROM", { applied: describeQuantities(applied), effectiveAtUtc: formatDate(applied.effectiveAtUtc) }),
      });

      onOpenChange(false);
    } catch (error) {
      const code = error instanceof SubscriptionOperationError ? error.code : "unknown";

      setFailure({ code, message: explain(code, error) });
      setQuote(null);

      // A stale version cannot be retried against what this dialog holds, so the subscription is
      // re-read and the subscriber previews again from the quantities that actually apply.
      if (code === "subscription_version_conflict" || code === "subscription_quantity_change_in_flight") {
        onRefresh();
      }
    }
  };

  const decrease = quote?.timing === "NextPeriod";

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
          <DialogTitle>{t("CHANGE_QUANTITY_DIALOG.CHANGE_QUANTITY")}</DialogTitle>
          <DialogDescription>
            {t("CHANGE_QUANTITY_DIALOG.AN_INCREASE_IS_CHARGED_FOR")}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="rounded-md border p-3 text-sm">
            <p className="font-medium">{t("CHANGE_QUANTITY_DIALOG.IN_FORCE_NOW")}</p>
            <p className="text-muted-foreground">
              {subscription.quantities
                .map((held) => `${held.quantity} ${held.unitLabel ?? held.itemKey}`)
                .join(", ") || "—"}
              {subscription.currentTier ? ` · ${describeTier(subscription.currentTier)}` : ""}
            </p>
            <p className="text-muted-foreground">
              {t("CHANGE_QUANTITY_DIALOG.AMOUNT_PER_PERIOD", { amount: formatMoney(subscription.recurringAmountMinor, subscription.currencyCode) })}
            </p>
          </div>

          {subscription.pendingQuantityChange ? (
            <div className="flex items-start gap-2 rounded-md border border-warning-300 bg-warning-50 p-3 text-sm">
              <CalendarClock className="mt-0.5 h-4 w-4 shrink-0 text-warning-800" />
              <p className="text-warning-900">
                {t("CHANGE_QUANTITY_DIALOG.REDUCTION_ALREADY_SCHEDULED", {
                  quantities: subscription.pendingQuantityChange.quantities
                    .map((entry) => `${entry.quantity} ${entry.unitLabel ?? entry.itemKey}`)
                    .join(", "),
                  date: formatDate(subscription.pendingQuantityChange.effectiveAtUtc),
                })}
              </p>
            </div>
          ) : null}

          {items.map((item) => (
            <div key={item.itemKey} className="space-y-1.5">
              <Label htmlFor={`quantity-${item.itemKey}`}>
                {item.unitLabel}s
                <span className="ml-2 text-xs text-muted-foreground">
                  {item.minQuantity}
                  {item.maxQuantity === null ? t("CHANGE_QUANTITY_DIALOG.OR_MORE") : `–${item.maxQuantity}`}
                </span>
              </Label>
              <Input
                id={`quantity-${item.itemKey}`}
                type="number"
                min={item.minQuantity}
                max={item.maxQuantity ?? undefined}
                value={targets[item.itemKey] ?? ""}
                onChange={(event) => edit(item.itemKey, event.target.value)}
              />
            </div>
          ))}

          {quote ? (
            <div className="space-y-2 rounded-md border p-3 text-sm">
              <div className="flex items-center justify-between">
                <p className="font-medium">
                  {decrease ? t("CHANGE_QUANTITY_DIALOG.SCHEDULED_FOR_THE_PERIOD_END") : t("CHANGE_QUANTITY_DIALOG.APPLIES_IMMEDIATELY")}
                </p>
                <Badge variant={decrease ? "secondary" : "default"}>
                  {decrease ? formatDate(quote.effectiveAtUtc) : t("CHANGE_QUANTITY_DIALOG.NOW")}
                </Badge>
              </div>

              <Row label={t("CHANGE_QUANTITY_DIALOG.NEW_QUANTITY")} value={describeQuantities(quote)} />
              <Row
                label={t("CHANGE_QUANTITY_DIALOG.VOLUME_BAND")}
                value={
                  quote.targetTier
                    ? describeTier(quote.targetTier)
                    : t("CHANGE_QUANTITY_DIALOG.NO_BAND")
                }
              />
              {/* The server's figure, not one derived here: the band alone does not determine it,
                  and a number that disagrees with the charge beside it is worst of all on the
                  screen where somebody confirms a payment. Absent on a flat fee, where nothing is
                  sold by the unit — printing the plan's whole price as the cost of "each" of a
                  freely tracked item would be worse than printing nothing. */}
              {quote.effectiveUnitAmountMinor !== null ? (
                <Row
                  label={t("CHANGE_QUANTITY_DIALOG.EFFECTIVE_UNIT_PRICE")}
                  value={t("CHANGE_QUANTITY_DIALOG.EACH_BEFORE_TAX", {
                    amount: formatMoney(quote.effectiveUnitAmountMinor, quote.currencyCode),
                  })}
                />
              ) : null}
              {/* Worded to what is actually on screen. A flat fee has neither a unit price nor a
                  band, so explaining that "the unit price is below the band's own reduction" there
                  described two things the card never showed — while still being worth saying that a
                  discount is in these figures at all. */}
              {quote.promotionApplied ? (
                <p className="text-xs text-muted-foreground">
                  {quote.effectiveUnitAmountMinor !== null
                    ? t("CHANGE_QUANTITY_DIALOG.INCLUDES_THE_DISCOUNT_ON_THIS")
                    : t("CHANGE_QUANTITY_DIALOG.INCLUDES_THE_PROMOTIONAL_DISCOUNT")}
                </p>
              ) : null}
              <Row
                label={decrease ? t("CHANGE_QUANTITY_DIALOG.CHARGED_NOW") : t("CHANGE_QUANTITY_DIALOG.PRORATED_CHARGE")}
                value={
                  quote.proratedChargeMinor > 0
                    ? formatMoney(quote.proratedChargeMinor, quote.currencyCode)
                    : t("CHANGE_QUANTITY_DIALOG.NOTHING")
                }
              />
              <Row
                label={quote.taxAmountMinor > 0 ? t("CHANGE_QUANTITY_DIALOG.NEXT_RENEWAL_INCL_TAX") : t("CHANGE_QUANTITY_DIALOG.NEXT_RENEWAL")}
                value={formatMoney(quote.nextRenewalAmountMinor, quote.currencyCode)}
              />
              {/* Named rather than folded into the unit price: tax is a proportion of the whole
                  charge, and a unit price carrying it read higher than the list price on a card
                  that also said the band took 5% off. */}
              {quote.taxAmountMinor > 0 ? (
                <Row
                  label={t("CHANGE_QUANTITY_DIALOG.OF_WHICH_TAX")}
                  value={formatMoney(quote.taxAmountMinor, quote.currencyCode)}
                />
              ) : null}

              {decrease ? (
                <p className="text-xs text-muted-foreground">
                  {t("CHANGE_QUANTITY_DIALOG.NOTHING_IS_REFUNDED_THE_UNITS", { effectiveAtUtc: formatDate(quote.effectiveAtUtc) })}</p>
              ) : null}
            </div>
          ) : null}

          {failure ? (
            <div className="flex items-start gap-2 rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" />
              <p className="text-destructive">{failure.message}</p>
            </div>
          ) : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={busy}>
            {t("common:CLOSE")}
          </Button>
          <Button variant="outline" onClick={() => run("preview")} disabled={busy}>
            {preview.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            {t("CHANGE_QUANTITY_DIALOG.PREVIEW")}
          </Button>
          {/* Nothing is charged without a quote on screen, and the quote is discarded the moment a
              quantity changes — so this button can only ever send the figures just shown. */}
          <Button onClick={() => run("apply")} disabled={busy || quote === null}>
            {apply.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            {decrease ? t("CHANGE_QUANTITY_DIALOG.SCHEDULE_CHANGE") : t("CHANGE_QUANTITY_DIALOG.CONFIRM_AND_PAY")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};

const Row = ({ label, value }: { label: string; value: string }) => (
  <div className="flex items-baseline justify-between gap-4">
    <span className="text-muted-foreground">{label}</span>
    <span className="text-right font-medium">{value}</span>
  </div>
);

const formatDate = (isoDate: string) => new Date(isoDate).toLocaleString();

const describeQuantities = (quote: QuantityChangeQuote) =>
  quote.quantities
    .map((entry) => `${entry.quantity} ${entry.unitLabel ?? entry.itemKey}`)
    .join(", ");

const describeTier = (tier: QuantityDiscountTier) => {
  const range =
    tier.maximumQuantity === null
      ? `${tier.minimumQuantity}+`
      : `${tier.minimumQuantity}–${tier.maximumQuantity}`;

  return tier.discountBasisPoints > 0
    ? tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.TIER_PERCENT_OFF", {
        range,
        percent: Number((tier.discountBasisPoints / 100).toFixed(2)),
      })
    : tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.TIER_NO_DISCOUNT", { range });
};

/**
 * What a subscriber should do about each outcome.
 *
 * The distinction that matters is between a refusal and an unanswered charge. A decline changed
 * nothing and can be retried; a charge whose outcome nobody knows must not be retried at all,
 * because the reservation behind it is still holding and the money may already have moved.
 */
const explain = (code: string, error: unknown): string => {
  switch (code) {
    case "subscription_version_conflict":
      return tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.THIS_SUBSCRIPTION_CHANGED_WHILE");
    case "subscription_quantity_change_in_flight":
      return tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.AN_EARLIER_CHANGE_IS_STILL");
    case "subscription_quantity_charge_failed":
      return tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.THE_SAVED_CARD_DECLINED_THE");
    // Not the card: the merchant's payment provider refused it. Another card fails the same way,
    // so the payer is told who has to act instead of being sent to change cards.
    case "subscription_quantity_charge_provider_misconfigured":
      return tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.THE_PAYMENT_PROVIDER_IS_NOT");
    case "subscription_quantity_charge_unresolved":
      return tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.THE_PAYMENT_PROVIDER_DID_NOT");
    case "subscription_payment_method_missing":
      return tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.AN_INCREASE_HAS_TO_BE");
    case "subscription_quantity_change_not_allowed":
      return tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.THIS_SUBSCRIPTION_CANNOT_CHANGE");
    case "subscription_cancellation_scheduled":
      return tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.THIS_SUBSCRIPTION_IS_SCHEDULED_TO");
    case "subscription_quantity_item_unknown":
      return tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.THIS_PLAN_DOES_NOT_SELL");
    case "subscription_quantity_unchanged":
      return tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.THAT_IS_ALREADY_THE_QUANTITY");
    case "subscription_quantity_invalid":
      return tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.THAT_QUANTITY_IS_OUTSIDE_WHAT");
    // Named apart from the generic conflict because the fix is a different action entirely:
    // release people from the members card, then come back. The server's own message carries
    // how many are in the way, so it is kept rather than replaced.
    case "subscription_member_seats_occupied":
      return error instanceof Error && error.message
        ? error.message
        : tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.SEATS_OCCUPIED");
    case "local_validation":
      return error instanceof Error ? error.message : tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.CHECK_THE_QUANTITY");
    default:
      return error instanceof Error ? error.message : tStatic("utilities.subscription-simulation", "CHANGE_QUANTITY_DIALOG.QUANTITY_NOT_CHANGED");
  }
};
