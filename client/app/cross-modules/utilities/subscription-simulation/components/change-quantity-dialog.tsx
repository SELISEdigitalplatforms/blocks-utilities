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
  open,
  onOpenChange,
  onRefresh,
}: {
  subscription: SimulatedSubscription;
  currentPlan: SubscriptionPlan | undefined;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onRefresh: () => void;
}) => {
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

        return {
          itemKey: held.itemKey,
          unitLabel: held.unitLabel ?? defined?.unitLabel ?? held.itemKey,
          held: held.quantity,
          minQuantity: defined?.minQuantity ?? 1,
          maxQuantity: defined?.maxQuantity ?? null,
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

  const requested = () => {
    const quantities: { itemKey: string; quantity: number }[] = [];

    for (const item of items) {
      const raw = targets[item.itemKey] ?? "";
      const quantity = Number(raw);

      if (!raw.trim() || !Number.isInteger(quantity)) {
        return { error: `Enter a whole number of ${item.unitLabel}s.` } as const;
      }

      if (quantity < item.minQuantity) {
        return {
          error: `This plan needs at least ${item.minQuantity} ${item.unitLabel}${
            item.minQuantity === 1 ? "" : "s"
          }.`,
        } as const;
      }

      if (item.maxQuantity !== null && quantity > item.maxQuantity) {
        return {
          error: `This plan allows at most ${item.maxQuantity} ${item.unitLabel}${
            item.maxQuantity === 1 ? "" : "s"
          }.`,
        } as const;
      }

      quantities.push({ itemKey: item.itemKey, quantity });
    }

    if (quantities.every((entry) => entry.quantity === items.find((item) => item.itemKey === entry.itemKey)?.held)) {
      return { error: "That is already the quantity in force." } as const;
    }

    return { quantities } as const;
  };

  const run = async (mode: "preview" | "apply") => {
    const parsed = requested();

    if ("error" in parsed) {
      setFailure({ code: "local_validation", message: parsed.error });

      return;
    }

    setFailure(null);

    const request = { version: subscription.version, quantities: parsed.quantities };

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
        title: applied.timing === "Immediate" ? "Quantity updated" : "Change scheduled",
        description:
          applied.timing === "Immediate"
            ? describeQuantities(applied)
            : `${describeQuantities(applied)} from ${formatDate(applied.effectiveAtUtc)}.`,
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
          <DialogTitle>Change quantity</DialogTitle>
          <DialogDescription>
            An increase is charged for the rest of the paid period and applies at once. A decrease
            keeps the units until the period ends, then bills the smaller quantity.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="rounded-md border p-3 text-sm">
            <p className="font-medium">In force now</p>
            <p className="text-muted-foreground">
              {subscription.quantities
                .map((held) => `${held.quantity} ${held.unitLabel ?? held.itemKey}`)
                .join(", ") || "—"}
              {subscription.currentTier ? ` · ${describeTier(subscription.currentTier)}` : ""}
            </p>
            <p className="text-muted-foreground">
              {formatMoney(subscription.recurringAmountMinor, subscription.currencyCode)} per period
            </p>
          </div>

          {subscription.pendingQuantityChange ? (
            <div className="flex items-start gap-2 rounded-md border border-warning-300 bg-warning-50 p-3 text-sm">
              <CalendarClock className="mt-0.5 h-4 w-4 shrink-0 text-warning-800" />
              <p className="text-warning-900">
                A reduction to{" "}
                {subscription.pendingQuantityChange.quantities
                  .map((entry) => `${entry.quantity} ${entry.unitLabel ?? entry.itemKey}`)
                  .join(", ")}{" "}
                is already scheduled for {formatDate(subscription.pendingQuantityChange.effectiveAtUtc)}.
                Confirming again replaces it.
              </p>
            </div>
          ) : null}

          {items.map((item) => (
            <div key={item.itemKey} className="space-y-1.5">
              <Label htmlFor={`quantity-${item.itemKey}`}>
                {item.unitLabel}s
                <span className="ml-2 text-xs text-muted-foreground">
                  {item.minQuantity}
                  {item.maxQuantity === null ? " or more" : `–${item.maxQuantity}`}
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
                  {decrease ? "Scheduled for the period end" : "Applies immediately"}
                </p>
                <Badge variant={decrease ? "secondary" : "default"}>
                  {decrease ? formatDate(quote.effectiveAtUtc) : "Now"}
                </Badge>
              </div>

              <Row label="New quantity" value={describeQuantities(quote)} />
              <Row
                label="Volume band"
                value={
                  quote.targetTier
                    ? describeTier(quote.targetTier)
                    : "No band — one price at every quantity"
                }
              />
              {quote.targetTier ? (
                <Row
                  label="Effective unit price"
                  value={`${formatMoney(
                    discounted(subscription.unitAmountMinor, quote.targetTier),
                    quote.currencyCode,
                  )} each`}
                />
              ) : null}
              <Row
                label={decrease ? "Charged now" : "Prorated charge"}
                value={
                  quote.proratedChargeMinor > 0
                    ? formatMoney(quote.proratedChargeMinor, quote.currencyCode)
                    : "Nothing"
                }
              />
              <Row
                label="Next renewal"
                value={formatMoney(quote.nextRenewalAmountMinor, quote.currencyCode)}
              />

              {decrease ? (
                <p className="text-xs text-muted-foreground">
                  Nothing is refunded: the units stay available until{" "}
                  {formatDate(quote.effectiveAtUtc)}, and the renewal then bills the smaller
                  quantity at its band.
                </p>
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
            Close
          </Button>
          <Button variant="outline" onClick={() => run("preview")} disabled={busy}>
            {preview.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            Preview
          </Button>
          {/* Nothing is charged without a quote on screen, and the quote is discarded the moment a
              quantity changes — so this button can only ever send the figures just shown. */}
          <Button onClick={() => run("apply")} disabled={busy || quote === null}>
            {apply.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            {decrease ? "Schedule change" : "Confirm and pay"}
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
    ? `${range} · ${Number((tier.discountBasisPoints / 100).toFixed(2))}% off`
    : `${range} · no discount`;
};

const discounted = (unitAmountMinor: number, tier: QuantityDiscountTier) =>
  Math.round(unitAmountMinor * (1 - tier.discountBasisPoints / 10_000));

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
      return "This subscription changed while you were deciding. It has been reloaded — preview the new quantity again.";
    case "subscription_quantity_change_in_flight":
      return "An earlier change is still being settled with the payment provider. Try again in a few minutes.";
    case "subscription_quantity_charge_failed":
      return "The saved card declined the charge. Nothing changed: the quantity is exactly as it was.";
    case "subscription_quantity_charge_unresolved":
      return "The payment provider did not answer, so whether the charge went through is not yet known. Do not try again — reload in a few minutes and the outcome will be settled by then.";
    case "subscription_payment_method_missing":
      return "An increase has to be charged, and there is no saved card to charge. Add a payment method first.";
    case "subscription_quantity_change_not_allowed":
      return "This subscription cannot change quantity in its current state.";
    case "subscription_quantity_item_unknown":
      return "This plan does not sell that item.";
    case "subscription_quantity_unchanged":
      return "That is already the quantity in force.";
    case "subscription_quantity_invalid":
      return "That quantity is outside what this plan allows.";
    case "local_validation":
      return error instanceof Error ? error.message : "Check the quantity and try again.";
    default:
      return error instanceof Error ? error.message : "The quantity could not be changed.";
  }
};
