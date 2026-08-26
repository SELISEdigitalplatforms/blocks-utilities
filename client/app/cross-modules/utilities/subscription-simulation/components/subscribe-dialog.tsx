import { AlertTriangle, Loader2 } from "lucide-react";
import { useState } from "react";
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
import { detectBrowserTimeZone } from "../constants/subscription-simulation.constants";
import { usePreviewSubscription } from "../hooks/use-preview-subscription";
import { useSubscribeToPlan } from "../hooks/use-subscribe-to-plan";
import type {
  SubscribeToPlanRequest,
  SubscriptionPurchasePreview,
  SubscriptionQuantity,
} from "../models/subscription-simulation.model";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import { formatMoney, formatPrice } from "../../subscription/utilities/subscription-format";
import {
  billingProfileGapOf,
  subscriptionApiFailure,
  type BillingProfileGap,
} from "../../subscription/utilities/subscription-api-failure";
import { BillingProfileIncompleteNotice } from "./billing-profile-incomplete-notice";

/**
 * Subscribing to a plan.
 *
 * Nothing is charged from this screen without a preview first, and any edit — the price, a
 * quantity, the discount code — discards the quote it produced: a confirmation sent after its
 * figures stopped applying would be a confirmation of numbers the subscriber never saw.
 */
export const SubscribeDialog = ({
  plan,
  organizationId,
  open,
  onOpenChange,
  onSubscribed,
}: {
  plan: SubscriptionPlan;
  organizationId: string | undefined;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSubscribed: (checkoutUrl: string | null) => void;
}) => {
  const preview = usePreviewSubscription();
  const subscribe = useSubscribeToPlan();

  const [priceId, setPriceId] = useState(plan.prices[0]?.priceId ?? "");
  const [quantities, setQuantities] = useState<Record<string, string>>(() =>
    Object.fromEntries(
      plan.quantityItems.map((item) => [item.itemKey, String(item.defaultQuantity)]),
    ),
  );
  const [discountCode, setDiscountCode] = useState("");
  const [quote, setQuote] = useState<SubscriptionPurchasePreview | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [confirmationProfileGap, setConfirmationProfileGap] =
    useState<BillingProfileGap | null>(null);

  const busy = preview.isPending || subscribe.isPending;

  const editPrice = (value: string) => {
    setPriceId(value);
    setQuote(null);
    setConfirmationProfileGap(null);
  };

  const editQuantity = (itemKey: string, value: string) => {
    setQuantities((current) => ({ ...current, [itemKey]: value }));
    setQuote(null);
    setConfirmationProfileGap(null);
  };

  const editDiscount = (value: string) => {
    setDiscountCode(value);
    setQuote(null);
    setConfirmationProfileGap(null);
  };

  const requested = ():
    | { valid: true; request: SubscribeToPlanRequest }
    | { valid: false; error: string } => {
    if (!priceId) {
      return { valid: false, error: "Choose a price to subscribe on." };
    }

    const parsedQuantities: SubscriptionQuantity[] = [];
    for (const item of plan.quantityItems) {
      const raw = quantities[item.itemKey] ?? "";
      const quantity = Number(raw);

      if (!raw || !Number.isFinite(quantity) || quantity < item.minQuantity) {
        return {
          valid: false,
          error: `${item.unitLabel} must be at least ${item.minQuantity}.`,
        };
      }

      if (item.maxQuantity != null && quantity > item.maxQuantity) {
        return {
          valid: false,
          error: `${item.unitLabel} can be at most ${item.maxQuantity}.`,
        };
      }

      parsedQuantities.push({ itemKey: item.itemKey, quantity });
    }

    return {
      valid: true,
      request: {
        planCode: plan.code,
        priceId,
        quantities: parsedQuantities,
        timeZoneId: detectBrowserTimeZone(),
        discountCode: discountCode.trim() || undefined,
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
      setQuote(await preview.mutateAsync(parsed.request));
    } catch (error) {
      setFormError(
        error instanceof Error ? error.message : "The subscription could not be previewed.",
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
      const subscription = await subscribe.mutateAsync(parsed.request);

      toast({
        variant: "success",
        title: "Subscription started",
        description: subscription.checkoutUrl
          ? "Continue to checkout to activate it."
          : `Status: ${subscription.status}.`,
      });

      onOpenChange(false);
      onSubscribed(subscription.checkoutUrl);
    } catch (error) {
      const failure = subscriptionApiFailure(error);
      const gap = billingProfileGapOf(failure);
      setConfirmationProfileGap(gap);
      setFormError(
        gap
          ? null
          : failure?.message ||
              (error instanceof Error
                ? error.message
                : "The subscription could not be started."),
      );
      // What was shown no longer describes what a retry would charge — the failed attempt may
      // itself have changed something a fresh quote needs to account for.
      setQuote(null);
    }
  };

  const blocked = (quote?.blockers.length ?? 0) > 0;
  const previewProfileGap = quote?.blockers
    .map((blocker) =>
      billingProfileGapOf({
        code: blocker.code,
        message: blocker.message,
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
          <DialogTitle>Subscribe to {plan.displayName}</DialogTitle>
          <DialogDescription>
            Sends <code>POST /api/subscriptions</code> exactly as an integrating application
            would.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="subscribe-price">Price</Label>
            <Select value={priceId} onValueChange={editPrice}>
              <SelectTrigger id="subscribe-price">
                <SelectValue placeholder="Choose a price" />
              </SelectTrigger>
              <SelectContent>
                {plan.prices.map((price) => (
                  <SelectItem key={price.priceId} value={price.priceId}>
                    {price.displayPriceNote ?? formatPrice(price)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {plan.quantityItems.map((item) => (
            <div className="space-y-1.5" key={item.itemKey}>
              <Label htmlFor={`quantity-${item.itemKey}`}>
                {item.unitLabel}
                {plan.prices.find((price) => price.priceId === priceId)?.quantityItemKey ===
                item.itemKey
                  ? " (price multiplier)"
                  : ""}
              </Label>
              <Input
                id={`quantity-${item.itemKey}`}
                type="number"
                min={item.minQuantity}
                max={item.maxQuantity ?? undefined}
                value={quantities[item.itemKey] ?? ""}
                onChange={(event) => editQuantity(item.itemKey, event.target.value)}
              />
            </div>
          ))}

          <div className="space-y-1.5">
            <Label htmlFor="subscribe-discount">Discount code (optional)</Label>
            <Input
              id="subscribe-discount"
              value={discountCode}
              onChange={(event) => editDiscount(event.target.value)}
              placeholder="e.g. LAUNCH20"
            />
          </div>

          {profileGap && (
            <BillingProfileIncompleteNotice gap={profileGap} organizationId={organizationId} />
          )}

          {quote ? (
            <div className="space-y-2 rounded-md border p-3 text-sm" data-testid="subscribe-quote">
              <div className="flex items-center justify-between">
                <p className="font-medium">
                  {quote.totalDueNowMinor > 0 ? "Due now" : "Nothing due now"}
                </p>
                <Badge variant={quote.prorated ? "secondary" : "default"}>
                  {quote.prorated ? `${quote.coveredDays}/${quote.totalDays} days` : "Full period"}
                </Badge>
              </div>

              <Row
                label="Total due now"
                value={formatMoney(quote.totalDueNowMinor, quote.currencyCode)}
              />
              {quote.discountMinor > 0 ? (
                <Row
                  label="Discount"
                  value={`-${formatMoney(quote.discountMinor, quote.currencyCode)}`}
                />
              ) : null}
              {quote.taxMinor > 0 ? (
                <Row label="of which tax" value={formatMoney(quote.taxMinor, quote.currencyCode)} />
              ) : null}
              <Row
                label={quote.trialEndsAtUtc ? "First renewal" : "Next renewal"}
                value={`${formatMoney(quote.nextRenewalAmountMinor, quote.currencyCode)}${
                  quote.nextRenewalAtUtc ? ` on ${formatDate(quote.nextRenewalAtUtc)}` : ""
                }`}
              />
              {quote.requiresCardSetup && quote.totalDueNowMinor === 0 ? (
                <p className="text-xs text-muted-foreground">
                  Nothing is charged now, but a card is required to start this subscription.
                </p>
              ) : null}
              {quote.pendingAnnualPeriod ? (
                <p className="text-xs text-muted-foreground">
                  Also buys the year starting {formatDate(quote.pendingAnnualPeriod.startUtc)}
                  {quote.pendingAnnualPeriod.collectedWithCheckout
                    ? " — included in the total above."
                    : ", collected separately when it starts."}
                </p>
              ) : null}
              {quote.quoteValidUntilUtc ? (
                <p className="text-xs text-muted-foreground">
                  This price holds until {formatDate(quote.quoteValidUntilUtc)}.
                </p>
              ) : null}

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
          {/* Nothing is charged without a quote on screen, and the quote is discarded the moment
              anything that would change the price is edited — so this button can only ever send
              the figures just shown. Still disabled on a blocker: the price was shown, but
              confirming it would be refused. */}
          <Button onClick={submit} disabled={busy || quote === null || blocked}>
            {subscribe.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            Subscribe
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
