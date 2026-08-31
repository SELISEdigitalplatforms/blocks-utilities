import { Info, Loader2 } from "lucide-react";
import { useState } from "react";
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
import { formatMoney } from "../../subscription/utilities/subscription-format";
import { usePreviewUsageOverage } from "../hooks/use-preview-usage-overage";
import type { MeterTerms, UsageOveragePreviewResult } from "../models/subscription-simulation.model";

/**
 * A whole, positive number of additional units -- anything else answers nothing, so the call is
 * never made for it. Parsed rather than validated with a regex: the input is a number field, and
 * what actually reaches the server has to survive `Number.isInteger`, not merely look like digits.
 */
const parseWholeQuantity = (raw: string): number | null => {
  if (raw.trim() === "") {
    return null;
  }

  const parsed = Number(raw);

  return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
};

const ChargeLine = ({
  label,
  amountMinor,
  currencyCode,
  emphasize,
}: {
  label: string;
  amountMinor: number;
  currencyCode: string;
  emphasize?: boolean;
}) => (
  <div className={`flex items-center justify-between ${emphasize ? "font-medium" : ""}`}>
    <span className={emphasize ? "" : "text-muted-foreground"}>{label}</span>
    <span>{formatMoney(amountMinor, currencyCode)}</span>
  </div>
);

const PreviewResult = ({ result }: { result: UsageOveragePreviewResult }) => (
  <div className="space-y-4 rounded-lg border bg-muted/30 p-3 text-sm">
    <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-xs text-muted-foreground">
      <span>Current usage</span>
      <span className="text-right text-foreground">
        {result.currentUsage.toLocaleString()} {result.unitLabel}
        {result.currentUsage === 1 ? "" : "s"}
      </span>
      <span>Projected usage</span>
      <span className="text-right text-foreground">
        {result.projectedUsage.toLocaleString()} {result.unitLabel}
        {result.projectedUsage === 1 ? "" : "s"}
      </span>
    </div>

    {result.additionalTierBreakdown.length > 0 && (
      <div className="space-y-1 border-t pt-2 text-xs">
        <p className="font-medium text-muted-foreground">Tier allocation</p>
        {result.additionalTierBreakdown.map((allocation, index) => (
          <div key={index} className="flex items-center justify-between">
            <span className="text-muted-foreground">
              {allocation.units.toLocaleString()} {result.unitLabel}
              {allocation.units === 1 ? "" : "s"} @{" "}
              {formatMoney(allocation.unitAmountMinor, result.currencyCode)}
            </span>
            <span>{formatMoney(allocation.amountMinor, result.currencyCode)}</span>
          </div>
        ))}
      </div>
    )}

    <div className="space-y-1 border-t pt-2">
      <p className="text-xs font-medium text-muted-foreground">Additional charge</p>
      <ChargeLine
        label="Gross"
        amountMinor={result.additionalCharge.grossMinor}
        currencyCode={result.currencyCode}
      />
      {result.additionalCharge.automaticDiscountMinor > 0 && (
        <ChargeLine
          label={`Discount${result.discount.automaticBasisPoints ? ` (${result.discount.automaticBasisPoints / 100}%)` : ""}`}
          amountMinor={-result.additionalCharge.automaticDiscountMinor}
          currencyCode={result.currencyCode}
        />
      )}
      {result.tax.rateBasisPoints != null && (
        <ChargeLine
          label={`Tax (${result.tax.rateBasisPoints / 100}%, ${result.tax.mode})`}
          amountMinor={result.additionalCharge.taxMinor}
          currencyCode={result.currencyCode}
        />
      )}
      <ChargeLine
        label="Additional charge"
        amountMinor={result.additionalCharge.totalMinor}
        currencyCode={result.currencyCode}
        emphasize
      />
    </div>

    <div className="border-t pt-2">
      <ChargeLine
        label="Projected period charge"
        amountMinor={result.projectedPeriodCharge.totalMinor}
        currencyCode={result.currencyCode}
        emphasize
      />
    </div>

    <div className="flex items-start gap-1.5 rounded-md bg-background p-2 text-xs text-muted-foreground">
      <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
      <span>
        {!result.writesUsage && !result.chargesPayment
          ? "Previewing neither records usage nor charges payment. "
          : ""}
        {result.finalChargeDependsOnActualPeriodEndUsage
          ? "The final invoice depends on actual usage recorded by period end, and may differ from this estimate."
          : ""}
      </span>
    </div>
  </div>
);

export const EstimateUsageDialog = ({
  meter,
  organizationId,
  open,
  onOpenChange,
}: {
  meter: MeterTerms;
  organizationId: string | undefined;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) => {
  const { mutateAsync, isPending, data: result, reset } = usePreviewUsageOverage();

  const [quantity, setQuantity] = useState("1");
  const [formError, setFormError] = useState<string | null>(null);

  const submit = async () => {
    const parsedQuantity = parseWholeQuantity(quantity);

    if (parsedQuantity === null) {
      setFormError("Enter a whole number of additional units greater than zero.");
      return;
    }

    setFormError(null);

    try {
      await mutateAsync({
        meterKey: meter.meterKey,
        additionalQuantity: parsedQuantity,
        organizationId,
      });
    } catch (error) {
      setFormError(error instanceof Error ? error.message : "The overage could not be previewed.");
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!isPending) {
          if (!next) {
            reset();
          }
          onOpenChange(next);
        }
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Estimate additional usage — {meter.displayName}</DialogTitle>
          <DialogDescription>
            Sends <code>POST /api/subscription-usage/overage/preview</code> — prices a
            hypothetical slice of additional usage with this subscription&apos;s own terms.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="estimate-usage-quantity">
              Additional {meter.unitLabel}s to estimate
            </Label>
            <Input
              id="estimate-usage-quantity"
              type="number"
              min={1}
              step={1}
              value={quantity}
              onChange={(event) => setQuantity(event.target.value)}
            />
          </div>

          {formError && <p className="text-sm text-destructive">{formError}</p>}

          {result && <PreviewResult result={result} />}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            Close
          </Button>
          <Button onClick={submit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Estimate
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
