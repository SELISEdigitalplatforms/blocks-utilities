import { Loader2 } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { toast } from "@/hooks/use-toast";
import { useCloseUsagePeriod } from "../hooks/use-close-usage-period";
import type {
  SimulatedRenewalOutcome,
  SubscriptionSimulationActionResponse,
} from "../models/subscription-simulation-harness.model";
import { useT, tStatic } from "@seliseblocks/genesis-os";

/** Not a server value — selecting this omits `paymentOutcome` so the real gateway decides. */
const REAL_GATEWAY = "RealGateway" as const;

const getOutcomes = (): { value: SimulatedRenewalOutcome | typeof REAL_GATEWAY; label: string }[] => [
  { value: REAL_GATEWAY, label: tStatic("utilities.subscription-simulation", "CLOSE_USAGE_PERIOD_DIALOG.REAL_PAYMENT_GATEWAY_NO_SCRIPT") },
  { value: "Succeeded", label: tStatic("utilities.subscription-simulation", "CLOSE_USAGE_PERIOD_DIALOG.SUCCEEDED") },
  { value: "Declined", label: tStatic("utilities.subscription-simulation", "CLOSE_USAGE_PERIOD_DIALOG.DECLINED") },
  { value: "InsufficientFunds", label: tStatic("utilities.subscription-simulation", "CLOSE_USAGE_PERIOD_DIALOG.INSUFFICIENT_FUNDS") },
  { value: "PaymentMethodExpired", label: tStatic("utilities.subscription-simulation", "CLOSE_USAGE_PERIOD_DIALOG.PAYMENT_METHOD_EXPIRED") },
  { value: "ProviderUnavailable", label: tStatic("utilities.subscription-simulation", "CLOSE_USAGE_PERIOD_DIALOG.PROVIDER_UNAVAILABLE") },
  { value: "OutcomeUnknown", label: tStatic("utilities.subscription-simulation", "CLOSE_USAGE_PERIOD_DIALOG.OUTCOME_UNKNOWN") },
];

export const CloseUsagePeriodDialog = ({
  subscriptionId,
  organizationId,
  open,
  onOpenChange,
  onResult,
}: {
  subscriptionId: string;
  organizationId: string | undefined;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onResult: (result: SubscriptionSimulationActionResponse) => void;
}) => {
  const { t } = useT("utilities.subscription-simulation");
  const { mutateAsync, isPending } = useCloseUsagePeriod();

  const [selection, setSelection] = useState<SimulatedRenewalOutcome | typeof REAL_GATEWAY>(
    REAL_GATEWAY,
  );
  const [chargeInvoice, setChargeInvoice] = useState(true);
  const [formError, setFormError] = useState<string | null>(null);

  const submit = async () => {
    setFormError(null);

    const paymentOutcome = selection === REAL_GATEWAY ? undefined : selection;

    try {
      const result = await mutateAsync({
        subscriptionId,
        request: { organizationId, paymentOutcome, chargeInvoice },
      });

      onResult(result);

      toast({
        variant: "success",
        title: t("CLOSE_USAGE_PERIOD_DIALOG.USAGE_PERIOD_CLOSED"),
        description: !chargeInvoice
          ? t("CLOSE_USAGE_PERIOD_DIALOG.NO_OVERAGE_INVOICE_WAS_CHARGED")
          : paymentOutcome
            ? t("CLOSE_USAGE_PERIOD_DIALOG.ANY_OVERAGE_INVOICE_WAS_CHARGED", { paymentOutcome })
            : t("CLOSE_USAGE_PERIOD_DIALOG.ANY_OVERAGE_INVOICE_WAS_CHARGED_2"),
      });

      onOpenChange(false);
    } catch (error) {
      setFormError(
        error instanceof Error ? error.message : t("CLOSE_USAGE_PERIOD_DIALOG.THE_USAGE_PERIOD_COULD_NOT"),
      );
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!isPending) {
          onOpenChange(next);
        }
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("CLOSE_USAGE_PERIOD_DIALOG.CLOSE_USAGE_PERIOD")}</DialogTitle>
          <DialogDescription>
            {t("CLOSE_USAGE_PERIOD_DIALOG.SENDS_CLOSES_THE_CURRENT_USAGE", { endpoint: `POST /api/subscription-simulation/subscriptions/${subscriptionId}/close-usage-period` })}</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="flex items-start gap-2">
            <Checkbox
              id="close-usage-period-charge"
              checked={chargeInvoice}
              onCheckedChange={(checked) => setChargeInvoice(checked === true)}
              className="mt-0.5"
            />
            <Label htmlFor="close-usage-period-charge" className="font-normal">
              {t("CLOSE_USAGE_PERIOD_DIALOG.ALSO_CHARGE_THE_OVERAGE_INVOICE")}
            </Label>
          </div>

          {chargeInvoice && (
            <div className="space-y-1.5">
              <Label htmlFor="close-usage-period-outcome">{t("CLOSE_USAGE_PERIOD_DIALOG.PAYMENT_OUTCOME")}</Label>
              <Select
                value={selection}
                onValueChange={(value) =>
                  setSelection(value as SimulatedRenewalOutcome | typeof REAL_GATEWAY)
                }
              >
                <SelectTrigger id="close-usage-period-outcome">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {getOutcomes().map((option) => (
                    <SelectItem key={option.value} value={option.value}>
                      {option.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}

          {formError && <p className="text-sm text-destructive">{formError}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            {t("common:CANCEL")}
          </Button>
          <Button onClick={submit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {t("CLOSE_USAGE_PERIOD_DIALOG.CLOSE_PERIOD")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
