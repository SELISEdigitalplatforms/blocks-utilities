import { AlertTriangle, Info, Loader2 } from "lucide-react";
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
import { formatPrice } from "../../subscription/utilities/subscription-format";
import {
  billingProfileGapOf,
  subscriptionApiFailure,
  type BillingProfileGap,
} from "../../subscription/utilities/subscription-api-failure";
import { BillingProfileIncompleteNotice } from "./billing-profile-incomplete-notice";
import { MoneyBreakdown, formatDate, formatDay } from "./money-breakdown";
import { useT, tStatic } from "@seliseblocks/genesis-os";

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
  const { t } = useT("utilities.subscription-simulation");
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
  // A rejected code is not an error here -- the quote beside it is the same subscription priced
  // without the code, and is still worth showing. Only the notice tells the subscriber why.
  const [discountNotice, setDiscountNotice] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [confirmationProfileGap, setConfirmationProfileGap] =
    useState<BillingProfileGap | null>(null);

  const busy = preview.isPending || subscribe.isPending;

  const editPrice = (value: string) => {
    setPriceId(value);
    setQuote(null);
    setDiscountNotice(null);
    setConfirmationProfileGap(null);
  };

  const editQuantity = (itemKey: string, value: string) => {
    setQuantities((current) => ({ ...current, [itemKey]: value }));
    setQuote(null);
    setDiscountNotice(null);
    setConfirmationProfileGap(null);
  };

  const editDiscount = (value: string) => {
    setDiscountCode(value);
    setQuote(null);
    setDiscountNotice(null);
    setConfirmationProfileGap(null);
  };

  const requested = ():
    | { valid: true; request: SubscribeToPlanRequest }
    | { valid: false; error: string } => {
    if (!priceId) {
      return { valid: false, error: t("SUBSCRIBE_DIALOG.CHOOSE_A_PRICE_TO_SUBSCRIBE") };
    }

    const parsedQuantities: SubscriptionQuantity[] = [];
    for (const item of plan.quantityItems) {
      const raw = quantities[item.itemKey] ?? "";
      const quantity = Number(raw);

      if (!raw || !Number.isFinite(quantity) || quantity < item.minQuantity) {
        return {
          valid: false,
          error: t("SUBSCRIBE_DIALOG.MUST_BE_AT_LEAST", { unitLabel: item.unitLabel, minQuantity: item.minQuantity }),
        };
      }

      if (item.maxQuantity != null && quantity > item.maxQuantity) {
        return {
          valid: false,
          error: t("SUBSCRIBE_DIALOG.CAN_BE_AT_MOST", { unitLabel: item.unitLabel, maxQuantity: item.maxQuantity }),
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
      const { status, message, quote: previewed } = await preview.mutateAsync(parsed.request);

      setQuote(previewed);
      // "Applied" is the only status that reduced anything; every other one leaves the quote
      // standing and needs saying, or the subscriber reads the undiscounted price as the
      // discounted one.
      setDiscountNotice(
        status && status !== "Applied"
          ? message || "That discount code could not be applied."
          : null,
      );
    } catch (error) {
      setFormError(
        error instanceof Error ? error.message : t("SUBSCRIBE_DIALOG.THE_SUBSCRIPTION_COULD_NOT_BE"),
      );
      setQuote(null);
      setDiscountNotice(null);
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
        title: t("SUBSCRIBE_DIALOG.SUBSCRIPTION_STARTED"),
        description: subscription.checkoutUrl
          ? t("SUBSCRIBE_DIALOG.CONTINUE_TO_CHECKOUT_TO_ACTIVATE")
          : t("SUBSCRIBE_DIALOG.STATUS", { status: subscription.status }),
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
                : t("SUBSCRIBE_DIALOG.THE_SUBSCRIPTION_COULD_NOT_BE_2")),
      );
      // What was shown no longer describes what a retry would charge — the failed attempt may
      // itself have changed something a fresh quote needs to account for.
      setQuote(null);
      setDiscountNotice(null);
    }
  };

  const blocked = (quote?.blockers.length ?? 0) > 0;
  // A row carrying a date must quote the charge that lands on that date. nextRenewal is the
  // steady-state recurring price; nextCharge is priced at its own instant. The trial-stub case
  // keeps nextRenewal: there this row is the recurring price that follows the dated stub charge
  // rendered above it, not the next charge itself.
  const recurring = quote
    ? quote.nextCharge.prorated
      ? quote.nextRenewal
      : quote.nextCharge
    : null;
  const dueNowParts = subtotalPartsOf(quote);
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
          <DialogTitle>{t("SUBSCRIBE_DIALOG.SUBSCRIBE_TO", { displayName: plan.displayName })}</DialogTitle>
          <DialogDescription>
            {t("SUBSCRIBE_DIALOG.SENDS_EXACTLY", { endpoint: "POST /api/subscriptions" })}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="subscribe-price">{t("SUBSCRIBE_DIALOG.PRICE")}</Label>
            <Select value={priceId} onValueChange={editPrice}>
              <SelectTrigger id="subscribe-price">
                <SelectValue placeholder={t("SUBSCRIBE_DIALOG.CHOOSE_A_PRICE")} />
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
                  ? t("SUBSCRIBE_DIALOG.PRICE_MULTIPLIER")
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
            <Label htmlFor="subscribe-discount">{t("SUBSCRIBE_DIALOG.DISCOUNT_CODE_OPTIONAL")}</Label>
            <Input
              id="subscribe-discount"
              value={discountCode}
              onChange={(event) => editDiscount(event.target.value)}
              placeholder={t("SUBSCRIBE_DIALOG.E_G_LAUNCH20")}
            />
            {discountNotice ? (
              <p
                className="flex items-start gap-1.5 text-xs text-warning-900"
                data-testid="subscribe-discount-notice"
              >
                <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                <span>{t("SUBSCRIBE_DIALOG.THE_PRICE_BELOW_EXCLUDES_IT", { discountNotice })}</span>
              </p>
            ) : null}
          </div>

          {profileGap && (
            <BillingProfileIncompleteNotice gap={profileGap} organizationId={organizationId} />
          )}

          {quote ? (
            <div className="space-y-2 rounded-md border p-3 text-sm" data-testid="subscribe-quote">
              {quote.trialAlreadyUsed ? (
                <p
                  className="flex items-start gap-1.5 text-xs text-muted-foreground"
                  data-testid="subscribe-trial-used-notice"
                >
                  <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                  <span>{t("SUBSCRIBE_DIALOG.YOU_HAVE_ALREADY_USED_THIS")}</span>
                </p>
              ) : null}
              <div className="flex items-center justify-between">
                <p className="font-medium">
                  {quote.totalDueNowMinor > 0 ? t("SUBSCRIBE_DIALOG.DUE_NOW") : t("SUBSCRIBE_DIALOG.NOTHING_DUE_NOW")}
                </p>
                <Badge variant={quote.prorated ? "secondary" : "default"}>
                  {quote.prorated ? t("SUBSCRIBE_DIALOG.DAYS", { coveredDays: (quote.coveredDays) ?? "", totalDays: (quote.totalDays) ?? "" }) : t("SUBSCRIBE_DIALOG.FULL_PERIOD")}
                </Badge>
              </div>

              <MoneyBreakdown
                currencyCode={quote.currencyCode}
                subtotalParts={dueNowParts}
                subtotalMinor={quote.subtotalMinor}
                builtInDiscountMinor={quote.builtInDiscountMinor}
                promotionalDiscountMinor={quote.promotionalDiscountMinor}
                netSubtotalMinor={quote.netSubtotalMinor}
                tax={quote.tax}
                totalLabel={t("SUBSCRIBE_DIALOG.TOTAL_DUE_NOW")}
                totalMinor={quote.totalDueNowMinor}
              />

              {quote.nextCharge.prorated ? (
                <div className="border-t pt-2">
                  <MoneyBreakdown
                    label={t("SUBSCRIBE_DIALOG.FIRST_CHARGE_AFTER_TRIAL")}
                    labelValue={formatDay(quote.nextCharge.chargeAtUtc)}
                    currencyCode={quote.currencyCode}
                    subtotalMinor={quote.nextCharge.subtotalMinor}
                    builtInDiscountMinor={quote.nextCharge.builtInDiscountMinor}
                    promotionalDiscountMinor={quote.nextCharge.promotionalDiscountMinor}
                    netSubtotalMinor={quote.nextCharge.netSubtotalMinor}
                    tax={quote.nextCharge.tax}
                    totalLabel={t("SUBSCRIBE_DIALOG.TOTAL")}
                    totalMinor={quote.nextCharge.totalMinor}
                  />
                  <p className="mt-1 text-xs text-muted-foreground">
                    {t("SUBSCRIBE_DIALOG.DAYS_THE_TRIAL_ENDS_MID", { coveredDays: (quote.nextCharge.coveredDays) ?? "", totalDays: (quote.nextCharge.totalDays) ?? "" })}</p>
                </div>
              ) : null}

              {recurring ? (
                <div className="border-t pt-2">
                  <MoneyBreakdown
                    label={
                      quote.nextCharge.prorated
                        ? t("SUBSCRIBE_DIALOG.RECURRING_PRICE")
                        : quote.trialEndsAtUtc
                          ? t("SUBSCRIBE_DIALOG.FIRST_RENEWAL")
                          : t("SUBSCRIBE_DIALOG.NEXT_RENEWAL")
                    }
                    labelValue={
                      quote.nextRenewal.renewalAtUtc
                        ? formatDay(quote.nextRenewal.renewalAtUtc)
                        : undefined
                    }
                    currencyCode={quote.currencyCode}
                    subtotalMinor={recurring.subtotalMinor}
                    builtInDiscountMinor={recurring.builtInDiscountMinor}
                    promotionalDiscountMinor={recurring.promotionalDiscountMinor}
                    netSubtotalMinor={recurring.netSubtotalMinor}
                    tax={recurring.tax}
                    totalLabel={t("SUBSCRIBE_DIALOG.TOTAL")}
                    totalMinor={recurring.totalMinor}
                  />
                </div>
              ) : null}

              {quote.requiresCardSetup && quote.totalDueNowMinor === 0 ? (
                <p className="text-xs text-muted-foreground">
                  {t("SUBSCRIBE_DIALOG.NOTHING_IS_CHARGED_NOW_BUT")}
                </p>
              ) : null}
              {quote.campaign ? (
                <div
                  className="space-y-1 rounded-md border border-blocks-primary-300 bg-blocks-primary-50 p-2 text-blocks-primary-900"
                  data-testid="subscribe-quote-campaign"
                >
                  <p>{quote.campaign.description}</p>
                  <p className="text-xs">
                    {t("SUBSCRIBE_DIALOG.STANDARD_PRICING_RESUMES", { discountEndsAtUtc: formatDate(quote.campaign.discountEndsAtUtc) })}</p>
                  {quote.campaign.temporaryEntitlementKey ? (
                    <p className="text-xs">
                      {t("SUBSCRIBE_DIALOG.LIMITED_TO_WHILE_THIS_OFFER", { temporaryEntitlementKey: quote.campaign.temporaryEntitlementKey, temporaryEntitlementLimit: (quote.campaign.temporaryEntitlementLimit) ?? "" })}</p>
                  ) : null}
                </div>
              ) : null}
              {quote.pendingAnnualPeriod ? (
                <p className="text-xs text-muted-foreground">
                  {(quote.pendingAnnualPeriod.collectedWithCheckout ? t("SUBSCRIBE_DIALOG.ALSO_BUYS_YEAR_INCLUDED", { startUtc: formatDate(quote.pendingAnnualPeriod.startUtc) }) : t("SUBSCRIBE_DIALOG.ALSO_BUYS_YEAR_SEPARATE", { startUtc: formatDate(quote.pendingAnnualPeriod.startUtc) }))}
                </p>
              ) : null}
              {quote.quoteValidUntilUtc ? (
                <p className="text-xs text-muted-foreground">
                  {t("SUBSCRIBE_DIALOG.THIS_PRICE_HOLDS_UNTIL", { quoteValidUntilUtc: formatDate(quote.quoteValidUntilUtc) })}</p>
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
            {t("common:CANCEL")}
          </Button>
          <Button variant="outline" onClick={runPreview} disabled={busy}>
            {preview.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            {t("SUBSCRIBE_DIALOG.PREVIEW")}
          </Button>
          {/* Nothing is charged without a quote on screen, and the quote is discarded the moment
              anything that would change the price is edited — so this button can only ever send
              the figures just shown. Still disabled on a blocker: the price was shown, but
              confirming it would be refused. */}
          <Button onClick={submit} disabled={busy || quote === null || blocked}>
            {subscribe.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            {t("SUBSCRIBE_DIALOG.SUBSCRIBE")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};

/**
 * The two things a calendar-aligned yearly subtotal is actually made of, or nothing.
 *
 * Only when the year is collected at checkout alongside a prorated opening stub: that is the one
 * case where one subtotal covers two periods, at two amounts, on two cadences. The stub's share is
 * the remainder once the year's own gross is taken off -- derived rather than read from a second
 * field, so it cannot disagree with the subtotal it is explaining.
 */
const subtotalPartsOf = (
  quote: SubscriptionPurchasePreview | null,
): { label: string; amountMinor: number }[] | undefined => {
  const annual = quote?.pendingAnnualPeriod;

  if (!quote || !annual?.collectedWithCheckout || !quote.prorated) {
    return undefined;
  }

  const stubMinor = quote.subtotalMinor - annual.grossAmountMinor;

  if (stubMinor <= 0) {
    return undefined;
  }

  return [
    {
      label: tStatic("utilities.subscription-simulation", "SUBSCRIBE_DIALOG.PRO_RATED", { periodStartUtc: formatDay(quote.periodStartUtc), periodEndUtc: formatDay(quote.periodEndUtc) }),
      amountMinor: stubMinor,
    },
    { label: tStatic("utilities.subscription-simulation", "SUBSCRIBE_DIALOG.YEAR_FROM", { startUtc: formatDay(annual.startUtc) }), amountMinor: annual.grossAmountMinor },
  ];
};
