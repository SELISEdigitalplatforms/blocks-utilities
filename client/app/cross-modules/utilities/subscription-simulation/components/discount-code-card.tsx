import { Loader2, TicketPercent } from "lucide-react";
import { useState } from "react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { detectBrowserTimeZone } from "../constants/subscription-simulation.constants";
import { usePreviewDiscountCode } from "../hooks/use-preview-discount-code";
import type { DiscountCodePreview } from "../models/subscription-simulation.model";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import { formatMoney, formatPrice } from "../../subscription/utilities/subscription-format";
import { MoneyBreakdown, formatDate } from "./money-breakdown";
import { useT, tStatic } from "@seliseblocks/genesis-os";

/**
 * How each verdict reads. The server's own status strings, kept verbatim as keys — an unmapped
 * one falls through to a neutral badge carrying the status itself rather than being swallowed,
 * because a status this screen does not recognize is still the answer the server gave.
 */
const getVerdicts = (): Record<string, { label: string; variant: "success" | "error" | "info" }> => ({
  Applied: { label: tStatic("utilities.subscription-simulation", "DISCOUNT_CODE_CARD.APPLIED"), variant: "success" },
  NotFound: { label: tStatic("utilities.subscription-simulation", "DISCOUNT_CODE_CARD.NO_SUCH_CODE"), variant: "error" },
  NotStarted: { label: tStatic("utilities.subscription-simulation", "DISCOUNT_CODE_CARD.NOT_STARTED_YET"), variant: "info" },
  Expired: { label: tStatic("utilities.subscription-simulation", "DISCOUNT_CODE_CARD.EXPIRED"), variant: "error" },
  NotApplicable: { label: tStatic("utilities.subscription-simulation", "DISCOUNT_CODE_CARD.NOT_APPLICABLE_HERE"), variant: "error" },
  AlreadyRedeemed: { label: tStatic("utilities.subscription-simulation", "DISCOUNT_CODE_CARD.ALREADY_REDEEMED"), variant: "error" },
  Unavailable: { label: tStatic("utilities.subscription-simulation", "DISCOUNT_CODE_CARD.TEMPORARILY_UNAVAILABLE"), variant: "info" },
});

/**
 * Trying a discount code out, without buying anything.
 *
 * The subscribe dialog can already carry a code, but only on the way to a real subscription —
 * and only for an organization that does not have one yet. Testing a code is a different
 * question: whether the code is live, what it is worth against a given price, and why it was
 * refused when it is refused. That question has no answer on this screen otherwise, which is
 * what this card is for.
 *
 * Nothing here is reserved: the endpoint prices the code and forgets it, so a campaign limited
 * to a fixed number of redemptions is not spent by being tested.
 */
export const DiscountCodeCard = ({
  plans,
  organizationId,
}: {
  plans: SubscriptionPlan[];
  organizationId: string | undefined;
}) => {
  const { t } = useT("utilities.subscription-simulation");
  const preview = usePreviewDiscountCode();

  const [planCode, setPlanCode] = useState(plans[0]?.code ?? "");
  const plan = plans.find((candidate) => candidate.code === planCode);
  const [priceId, setPriceId] = useState(plans[0]?.prices[0]?.priceId ?? "");
  const [code, setCode] = useState("");
  const [result, setResult] = useState<DiscountCodePreview | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  // Any edit discards the verdict it produced: a code shown as applied to one price says nothing
  // about another, and leaving it on screen would read as though it did.
  const editPlan = (value: string) => {
    setPlanCode(value);
    setPriceId(plans.find((candidate) => candidate.code === value)?.prices[0]?.priceId ?? "");
    setResult(null);
    setFormError(null);
  };

  const edit = (apply: () => void) => {
    apply();
    setResult(null);
    setFormError(null);
  };

  const test = async () => {
    if (!plan || !priceId) {
      setFormError(t("DISCOUNT_CODE_CARD.CHOOSE_A_PLAN_AND_A"));
      return;
    }

    if (!code.trim()) {
      setFormError(t("DISCOUNT_CODE_CARD.ENTER_A_DISCOUNT_CODE"));
      return;
    }

    setFormError(null);

    try {
      setResult(
        await preview.mutateAsync({
          planCode: plan.code,
          priceId,
          // The plan's own defaults: this card asks whether a code is live and what it is worth,
          // not what a particular basket costs. Quantities are editable in the subscribe dialog
          // for the purchase that follows.
          quantities: plan.quantityItems.map((item) => ({
            itemKey: item.itemKey,
            quantity: item.defaultQuantity,
          })),
          timeZoneId: detectBrowserTimeZone(),
          discountCode: code.trim(),
          organizationId,
        }),
      );
    } catch (error) {
      setResult(null);
      setFormError(
        error instanceof Error ? error.message : t("DISCOUNT_CODE_CARD.THE_DISCOUNT_CODE_COULD_NOT"),
      );
    }
  };

  const verdict = result ? getVerdicts()[result.status] : null;
  const quote = result?.quote;

  return (
    <Card className="rounded-xl p-0">
      <div className="border-b p-4 sm:p-5">
        <h2 className="flex items-center gap-2 font-semibold">
          <TicketPercent className="h-4 w-4" />
          {t("DISCOUNT_CODE_CARD.DISCOUNT_CODE")}
        </h2>
        <p className="text-xs text-muted-foreground">
          {t("DISCOUNT_CODE_CARD.CHECKS_ONE_CODE", { endpoint: "POST /api/subscription-discounts/preview" })}
        </p>
      </div>

      <div className="space-y-4 p-4 sm:p-5">
        <div className="grid gap-4 sm:grid-cols-3">
          <div className="space-y-1.5">
            <Label htmlFor="discount-test-plan">{t("DISCOUNT_CODE_CARD.PLAN")}</Label>
            <Select value={planCode} onValueChange={editPlan}>
              <SelectTrigger id="discount-test-plan">
                <SelectValue placeholder={t("DISCOUNT_CODE_CARD.CHOOSE_A_PLAN")} />
              </SelectTrigger>
              <SelectContent>
                {plans.map((candidate) => (
                  <SelectItem key={candidate.planId} value={candidate.code}>
                    {candidate.displayName}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="discount-test-price">{t("DISCOUNT_CODE_CARD.PRICE")}</Label>
            <Select value={priceId} onValueChange={(value) => edit(() => setPriceId(value))}>
              <SelectTrigger id="discount-test-price">
                <SelectValue placeholder={t("DISCOUNT_CODE_CARD.CHOOSE_A_PRICE")} />
              </SelectTrigger>
              <SelectContent>
                {(plan?.prices ?? []).map((price) => (
                  <SelectItem key={price.priceId} value={price.priceId}>
                    {price.displayPriceNote ?? formatPrice(price)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="discount-test-code">{t("DISCOUNT_CODE_CARD.CODE")}</Label>
            <Input
              id="discount-test-code"
              value={code}
              onChange={(event) => edit(() => setCode(event.target.value))}
              placeholder={t("DISCOUNT_CODE_CARD.E_G_LAUNCH20")}
            />
          </div>
        </div>

        <Button onClick={test} disabled={preview.isPending}>
          {preview.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
          {t("DISCOUNT_CODE_CARD.TEST_CODE")}
        </Button>

        {result && quote ? (
          <div className="space-y-2 rounded-md border p-3 text-sm" data-testid="discount-verdict">
            <div className="flex items-center justify-between gap-2">
              <p className="font-medium">
                {result.status === "Applied"
                  ? t("DISCOUNT_CODE_CARD.WORTH_ON_THIS_PRICE", { promotionalDiscountMinor: formatMoney(quote.promotionalDiscountMinor, quote.currencyCode) })
                  : t("DISCOUNT_CODE_CARD.NOT_APPLIED_THE_STANDARD_PRICE")}
              </p>
              <Badge variant={verdict?.variant ?? "info"}>
                {verdict?.label ?? result.status}
              </Badge>
            </div>

            {result.message ? <p className="text-muted-foreground">{result.message}</p> : null}
            {/* The same code a confirming subscribe call would fail with -- the thing an
                integrator actually has to handle, and not reconstructible from the sentence. */}
            {result.reasonCode ? (
              <p className="text-xs text-muted-foreground">
                {t("DISCOUNT_CODE_CARD.REASON_CODE_VALUE", { reasonCode: result.reasonCode })}
              </p>
            ) : null}

            <div className="border-t pt-2">
              <MoneyBreakdown
                currencyCode={quote.currencyCode}
                subtotalMinor={quote.subtotalMinor}
                builtInDiscountMinor={quote.builtInDiscountMinor}
                promotionalDiscountMinor={quote.promotionalDiscountMinor}
                netSubtotalMinor={quote.netSubtotalMinor}
                tax={quote.tax}
                totalLabel={t("DISCOUNT_CODE_CARD.TOTAL_DUE_NOW")}
                totalMinor={quote.totalDueNowMinor}
              />
            </div>

            <div className="border-t pt-2">
              <MoneyBreakdown
                label={quote.trialEndsAtUtc ? t("DISCOUNT_CODE_CARD.FIRST_RENEWAL") : t("DISCOUNT_CODE_CARD.NEXT_RENEWAL")}
                labelValue={
                  quote.nextRenewal.renewalAtUtc
                    ? formatDate(quote.nextRenewal.renewalAtUtc)
                    : undefined
                }
                currencyCode={quote.currencyCode}
                subtotalMinor={quote.nextRenewal.subtotalMinor}
                builtInDiscountMinor={quote.nextRenewal.builtInDiscountMinor}
                promotionalDiscountMinor={quote.nextRenewal.promotionalDiscountMinor}
                netSubtotalMinor={quote.nextRenewal.netSubtotalMinor}
                tax={quote.nextRenewal.tax}
                totalLabel={t("DISCOUNT_CODE_CARD.TOTAL")}
                totalMinor={quote.nextRenewal.totalMinor}
              />
            </div>

            {quote.campaign ? (
              <div className="space-y-1 rounded-md border border-blocks-primary-300 bg-blocks-primary-50 p-2 text-blocks-primary-900">
                <p>{quote.campaign.description}</p>
                <p className="text-xs">
                  {t("DISCOUNT_CODE_CARD.STANDARD_PRICING_RESUMES", { discountEndsAtUtc: formatDate(quote.campaign.discountEndsAtUtc) })}</p>
                {quote.campaign.temporaryEntitlementKey ? (
                  <p className="text-xs">
                    {t("DISCOUNT_CODE_CARD.LIMITED_TO_WHILE_THIS_OFFER", { temporaryEntitlementKey: quote.campaign.temporaryEntitlementKey, temporaryEntitlementLimit: (quote.campaign.temporaryEntitlementLimit) ?? "" })}</p>
                ) : null}
              </div>
            ) : null}

            {/* A blocker on the quote -- an already-live subscription, an incomplete billing
                profile -- would stop the purchase, never the code itself, so it is reported
                without touching the verdict above. */}
            {quote.blockers.map((blocker) => (
              <p key={blocker.code} className="text-xs text-muted-foreground">
                {t("DISCOUNT_CODE_CARD.WOULD_BLOCK_A_REAL_SIGNUP", { message: blocker.message })}
              </p>
            ))}
          </div>
        ) : null}

        {formError ? <p className="text-sm text-destructive">{formError}</p> : null}
      </div>
    </Card>
  );
};
