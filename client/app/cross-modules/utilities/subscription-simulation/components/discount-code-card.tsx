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

/**
 * How each verdict reads. The server's own status strings, kept verbatim as keys — an unmapped
 * one falls through to a neutral badge carrying the status itself rather than being swallowed,
 * because a status this screen does not recognize is still the answer the server gave.
 */
const VERDICTS: Record<string, { label: string; variant: "success" | "error" | "info" }> = {
  Applied: { label: "Applied", variant: "success" },
  NotFound: { label: "No such code", variant: "error" },
  NotStarted: { label: "Not started yet", variant: "info" },
  Expired: { label: "Expired", variant: "error" },
  NotApplicable: { label: "Not applicable here", variant: "error" },
  AlreadyRedeemed: { label: "Already redeemed", variant: "error" },
  Unavailable: { label: "Temporarily unavailable", variant: "info" },
};

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
      setFormError("Choose a plan and a price to test the code against.");
      return;
    }

    if (!code.trim()) {
      setFormError("Enter a discount code.");
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
        error instanceof Error ? error.message : "The discount code could not be checked.",
      );
    }
  };

  const verdict = result ? VERDICTS[result.status] : null;
  const quote = result?.quote;

  return (
    <Card className="rounded-xl p-0">
      <div className="border-b p-4 sm:p-5">
        <h2 className="flex items-center gap-2 font-semibold">
          <TicketPercent className="h-4 w-4" />
          Discount code
        </h2>
        <p className="text-xs text-muted-foreground">
          Checks one code against a plan and price through
          <code className="mx-1 rounded bg-muted px-1">
            POST /api/subscription-discounts/preview
          </code>
          — the same validation a real signup runs. Nothing is charged and no redemption is
          reserved, so a limited campaign is not spent by testing it.
        </p>
      </div>

      <div className="space-y-4 p-4 sm:p-5">
        <div className="grid gap-4 sm:grid-cols-3">
          <div className="space-y-1.5">
            <Label htmlFor="discount-test-plan">Plan</Label>
            <Select value={planCode} onValueChange={editPlan}>
              <SelectTrigger id="discount-test-plan">
                <SelectValue placeholder="Choose a plan" />
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
            <Label htmlFor="discount-test-price">Price</Label>
            <Select value={priceId} onValueChange={(value) => edit(() => setPriceId(value))}>
              <SelectTrigger id="discount-test-price">
                <SelectValue placeholder="Choose a price" />
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
            <Label htmlFor="discount-test-code">Code</Label>
            <Input
              id="discount-test-code"
              value={code}
              onChange={(event) => edit(() => setCode(event.target.value))}
              placeholder="e.g. LAUNCH20"
            />
          </div>
        </div>

        <Button onClick={test} disabled={preview.isPending}>
          {preview.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
          Test code
        </Button>

        {result && quote ? (
          <div className="space-y-2 rounded-md border p-3 text-sm" data-testid="discount-verdict">
            <div className="flex items-center justify-between gap-2">
              <p className="font-medium">
                {result.status === "Applied"
                  ? `Worth ${formatMoney(quote.promotionalDiscountMinor, quote.currencyCode)} on this price`
                  : "Not applied — the standard price is shown"}
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
                Reason code <code className="rounded bg-muted px-1">{result.reasonCode}</code>
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
                totalLabel="Total due now"
                totalMinor={quote.totalDueNowMinor}
              />
            </div>

            <div className="border-t pt-2">
              <MoneyBreakdown
                label={quote.trialEndsAtUtc ? "First renewal" : "Next renewal"}
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
                totalLabel="Total"
                totalMinor={quote.nextRenewal.totalMinor}
              />
            </div>

            {quote.campaign ? (
              <div className="space-y-1 rounded-md border border-blocks-primary-300 bg-blocks-primary-50 p-2 text-blocks-primary-900">
                <p>{quote.campaign.description}</p>
                <p className="text-xs">
                  Standard pricing resumes {formatDate(quote.campaign.discountEndsAtUtc)}.
                </p>
                {quote.campaign.temporaryEntitlementKey ? (
                  <p className="text-xs">
                    {quote.campaign.temporaryEntitlementKey} limited to{" "}
                    {quote.campaign.temporaryEntitlementLimit} while this offer runs.
                  </p>
                ) : null}
              </div>
            ) : null}

            {/* A blocker on the quote -- an already-live subscription, an incomplete billing
                profile -- would stop the purchase, never the code itself, so it is reported
                without touching the verdict above. */}
            {quote.blockers.map((blocker) => (
              <p key={blocker.code} className="text-xs text-muted-foreground">
                Would block a real signup: {blocker.message}
              </p>
            ))}
          </div>
        ) : null}

        {formError ? <p className="text-sm text-destructive">{formError}</p> : null}
      </div>
    </Card>
  );
};
