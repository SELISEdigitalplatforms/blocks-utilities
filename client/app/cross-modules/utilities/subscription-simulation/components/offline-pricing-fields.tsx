import { Landmark } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { useT } from "@seliseblocks/genesis-os";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { minorUnitStep } from "../../subscription/utilities/subscription-format";
import type { OfflinePricingMode } from "../models/subscription-simulation.model";

const MODES: OfflinePricingMode[] = ["Catalogue", "Contract", "None"];

/** The pricing mode and, for a contract, its flat net amount per interval. Shared by activation and re-pricing. */
export const OfflinePricingFields = ({
  idPrefix,
  mode,
  contractAmount,
  currencyCode,
  onModeChange,
  onContractAmountChange,
}: {
  idPrefix: string;
  mode: OfflinePricingMode;
  contractAmount: string;
  currencyCode: string;
  onModeChange: (mode: OfflinePricingMode) => void;
  onContractAmountChange: (amount: string) => void;
}) => {
  const { t } = useT("utilities.subscription-simulation");

  return (
    <div className="space-y-3">
      <div className="space-y-1.5">
        <Label htmlFor={`${idPrefix}-mode`}>{t("OFFLINE_PRICING.MODE")}</Label>
        <Select value={mode} onValueChange={(value) => onModeChange(value as OfflinePricingMode)}>
          <SelectTrigger id={`${idPrefix}-mode`}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {MODES.map((value) => (
              <SelectItem key={value} value={value}>
                {t(`OFFLINE_PRICING.MODE_${value.toUpperCase()}`)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <p className="text-xs text-muted-foreground">{t(`OFFLINE_PRICING.MODE_${mode.toUpperCase()}_HINT`)}</p>
      </div>

      {mode === "Contract" && (
        <div className="space-y-1.5">
          <Label htmlFor={`${idPrefix}-amount`}>
            {t("OFFLINE_PRICING.CONTRACT_AMOUNT", { currencyCode })}
          </Label>
          <Input
            id={`${idPrefix}-amount`}
            type="number"
            min={0}
            step={minorUnitStep(currencyCode)}
            value={contractAmount}
            onChange={(event) => onContractAmountChange(event.target.value)}
          />
        </div>
      )}
    </div>
  );
};

/** Shown wherever the customer's subscription is: it is paid by bank transfer and never takes a card. */
export const BilledByInvoiceBadge = () => {
  const { t } = useT("utilities.subscription-simulation");
  return (
    <Badge variant="secondary" className="font-normal" data-testid="billed-by-invoice">
      <Landmark className="mr-1 h-3 w-3" />
      {t("OFFLINE_PRICING.BILLED_BY_INVOICE")}
    </Badge>
  );
};
