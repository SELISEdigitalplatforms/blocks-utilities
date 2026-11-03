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
import {
  isWithinMagnitude,
  isWithinScale,
  stepFor,
} from "../../subscription/utilities/meter-quantity";
import { formatMoney } from "../../subscription/utilities/subscription-format";
import { usePreviewUsageOverage } from "../hooks/use-preview-usage-overage";
import type { MeterTerms, UsageOveragePreviewResult } from "../models/subscription-simulation.model";
import { useT } from "@seliseblocks/genesis-os";

/**
 * A positive quantity the meter can actually hold -- anything else answers nothing, so the call is
 * never made for it. Held to the meter's own `quantityScale`, the same rule the server applies, so
 * a fractional meter accepts 500.5 and a whole-unit meter still refuses it.
 */
const parseQuantity = (raw: string, scale: number): number | null => {
  if (raw.trim() === "") {
    return null;
  }

  const parsed = Number(raw);

  return parsed > 0 && isWithinScale(parsed, scale) && isWithinMagnitude(parsed) ? parsed : null;
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

const PreviewResult = ({ result }: { result: UsageOveragePreviewResult }) => {
  const { t } = useT("utilities.subscription-simulation");
  return (
  <div className="space-y-4 rounded-lg border bg-muted/30 p-3 text-sm">
    <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-xs text-muted-foreground">
      <span>{t("ESTIMATE_USAGE_DIALOG.CURRENT_USAGE")}</span>
      <span className="text-right text-foreground">
        {result.currentUsage.toLocaleString()} {result.unitLabel}
        {result.currentUsage === 1 ? "" : "s"}
      </span>
      <span>{t("ESTIMATE_USAGE_DIALOG.PROJECTED_USAGE")}</span>
      <span className="text-right text-foreground">
        {result.projectedUsage.toLocaleString()} {result.unitLabel}
        {result.projectedUsage === 1 ? "" : "s"}
      </span>
    </div>

    {result.additionalTierBreakdown.length > 0 && (
      <div className="space-y-1 border-t pt-2 text-xs">
        <p className="font-medium text-muted-foreground">{t("ESTIMATE_USAGE_DIALOG.TIER_ALLOCATION")}</p>
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
      <p className="text-xs font-medium text-muted-foreground">{t("ESTIMATE_USAGE_DIALOG.ADDITIONAL_CHARGE")}</p>
      <ChargeLine
        label={t("ESTIMATE_USAGE_DIALOG.GROSS")}
        amountMinor={result.additionalCharge.grossMinor}
        currencyCode={result.currencyCode}
      />
      {result.additionalCharge.automaticDiscountMinor > 0 && (
        <ChargeLine
          label={result.discount.automaticBasisPoints ? t("ESTIMATE_USAGE_DIALOG.DISCOUNT_PERCENT", { percent: result.discount.automaticBasisPoints / 100 }) : t("ESTIMATE_USAGE_DIALOG.DISCOUNT")}
          amountMinor={-result.additionalCharge.automaticDiscountMinor}
          currencyCode={result.currencyCode}
        />
      )}
      {result.tax.rateBasisPoints != null && (
        <ChargeLine
          label={t("ESTIMATE_USAGE_DIALOG.TAX", { percent: result.tax.rateBasisPoints / 100, mode: result.tax.mode })}
          amountMinor={result.additionalCharge.taxMinor}
          currencyCode={result.currencyCode}
        />
      )}
      <ChargeLine
        label={t("ESTIMATE_USAGE_DIALOG.ADDITIONAL_CHARGE")}
        amountMinor={result.additionalCharge.totalMinor}
        currencyCode={result.currencyCode}
        emphasize
      />
    </div>

    <div className="border-t pt-2">
      <ChargeLine
        label={t("ESTIMATE_USAGE_DIALOG.PROJECTED_PERIOD_CHARGE")}
        amountMinor={result.projectedPeriodCharge.totalMinor}
        currencyCode={result.currencyCode}
        emphasize
      />
    </div>

    <div className="flex items-start gap-1.5 rounded-md bg-background p-2 text-xs text-muted-foreground">
      <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
      <span>
        {!result.writesUsage && !result.chargesPayment
          ? t("ESTIMATE_USAGE_DIALOG.PREVIEWING_NEITHER_RECORDS_USAGE")
          : ""}
        {result.finalChargeDependsOnActualPeriodEndUsage
          ? t("ESTIMATE_USAGE_DIALOG.THE_FINAL_INVOICE_DEPENDS_ON")
          : ""}
      </span>
    </div>
  </div>
);
};

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
  const { t } = useT("utilities.subscription-simulation");
  const { mutateAsync, isPending, data: result, reset } = usePreviewUsageOverage();

  const [quantity, setQuantity] = useState("1");
  const [formError, setFormError] = useState<string | null>(null);

  const scale = meter.quantityScale ?? 0;

  const submit = async () => {
    const parsedQuantity = parseQuantity(quantity, scale);

    if (parsedQuantity === null) {
      setFormError(
        scale === 0
          ? t("ESTIMATE_USAGE_DIALOG.ENTER_A_WHOLE_NUMBER_OF")
          : t("ESTIMATE_USAGE_DIALOG.ENTER_A_QUANTITY_GREATER_THAN", { scale }),
      );
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
      setFormError(error instanceof Error ? error.message : t("ESTIMATE_USAGE_DIALOG.THE_OVERAGE_COULD_NOT_BE"));
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
          <DialogTitle>{t("ESTIMATE_USAGE_DIALOG.ESTIMATE_ADDITIONAL_USAGE", { displayName: meter.displayName })}</DialogTitle>
          <DialogDescription>
            {t("ESTIMATE_USAGE_DIALOG.SENDS_PRICES_A_SLICE", { endpoint: "POST /api/subscription-usage/overage/preview" })}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="estimate-usage-quantity">
              {t("ESTIMATE_USAGE_DIALOG.ADDITIONAL_S_TO_ESTIMATE", { unitLabel: meter.unitLabel })}</Label>
            <Input
              id="estimate-usage-quantity"
              type="number"
              min={stepFor(scale)}
              step={stepFor(scale)}
              value={quantity}
              onChange={(event) => setQuantity(event.target.value)}
            />
          </div>

          {formError && <p className="text-sm text-destructive">{formError}</p>}

          {result && <PreviewResult result={result} />}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            {t("common:CLOSE")}
          </Button>
          <Button onClick={submit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {t("ESTIMATE_USAGE_DIALOG.ESTIMATE")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
