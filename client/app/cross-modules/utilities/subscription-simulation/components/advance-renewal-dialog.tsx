import { Loader2 } from "lucide-react";
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
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { toast } from "@/hooks/use-toast";
import { useAdvanceRenewal } from "../hooks/use-advance-renewal";
import type {
  SimulatedRenewalOutcome,
  SubscriptionSimulationActionResponse,
} from "../models/subscription-simulation-harness.model";
import { useT, tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_SUBSCRIPTION_SIMULATION_MODULE = "utilities.subscription-simulation";

/** Not a server value — selecting this omits `paymentOutcome` so the real gateway decides. */
const REAL_GATEWAY = "RealGateway" as const;

const getOutcomes = (): { value: SimulatedRenewalOutcome | typeof REAL_GATEWAY; label: string }[] => [
  { value: REAL_GATEWAY, label: tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "ADVANCE_RENEWAL_DIALOG.REAL_PAYMENT_GATEWAY_NO_SCRIPT") },
  { value: "Succeeded", label: tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "ADVANCE_RENEWAL_DIALOG.SUCCEEDED") },
  { value: "Declined", label: tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "ADVANCE_RENEWAL_DIALOG.DECLINED") },
  { value: "InsufficientFunds", label: tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "ADVANCE_RENEWAL_DIALOG.INSUFFICIENT_FUNDS") },
  { value: "PaymentMethodExpired", label: tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "ADVANCE_RENEWAL_DIALOG.PAYMENT_METHOD_EXPIRED") },
  { value: "ProviderUnavailable", label: tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "ADVANCE_RENEWAL_DIALOG.PROVIDER_UNAVAILABLE") },
  { value: "OutcomeUnknown", label: tStatic(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE, "ADVANCE_RENEWAL_DIALOG.OUTCOME_UNKNOWN") },
];

export const AdvanceRenewalDialog = ({
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
  const { t } = useT(UTILITIES_SUBSCRIPTION_SIMULATION_MODULE);
  const { mutateAsync, isPending } = useAdvanceRenewal();

  const [selection, setSelection] = useState<SimulatedRenewalOutcome | typeof REAL_GATEWAY>(
    REAL_GATEWAY,
  );
  const [formError, setFormError] = useState<string | null>(null);

  const submit = async () => {
    setFormError(null);

    const paymentOutcome = selection === REAL_GATEWAY ? undefined : selection;

    try {
      const result = await mutateAsync({
        subscriptionId,
        request: { organizationId, paymentOutcome },
      });

      onResult(result);

      toast({
        variant: paymentOutcome && paymentOutcome !== "Succeeded" ? "destructive" : "success",
        title: t("ADVANCE_RENEWAL_DIALOG.RENEWAL_ADVANCED"),
        description: paymentOutcome
          ? t("ADVANCE_RENEWAL_DIALOG.CHARGED_AS", { paymentOutcome })
          : t("ADVANCE_RENEWAL_DIALOG.CHARGED_AGAINST_THE_REAL_PAYMENT"),
      });

      onOpenChange(false);
    } catch (error) {
      setFormError(error instanceof Error ? error.message : t("ADVANCE_RENEWAL_DIALOG.THE_RENEWAL_COULD_NOT_BE"));
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
          <DialogTitle>{t("ADVANCE_RENEWAL_DIALOG.ADVANCE_RENEWAL")}</DialogTitle>
          <DialogDescription>
            {t("ADVANCE_RENEWAL_DIALOG.SENDS_FORCES_AN_IMMEDIATE_RENEWAL", { endpoint: `POST /api/subscription-simulation/subscriptions/${subscriptionId}/advance-renewal` })}</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="advance-renewal-outcome">{t("ADVANCE_RENEWAL_DIALOG.PAYMENT_OUTCOME")}</Label>
            <Select
              value={selection}
              onValueChange={(value) =>
                setSelection(value as SimulatedRenewalOutcome | typeof REAL_GATEWAY)
              }
            >
              <SelectTrigger id="advance-renewal-outcome">
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

          <p className="text-xs text-muted-foreground">
            {(selection === REAL_GATEWAY ? t("ADVANCE_RENEWAL_DIALOG.ADVANCES_REAL_GATEWAY") : t("ADVANCE_RENEWAL_DIALOG.ADVANCES_SCRIPTED"))}
          </p>

          {formError && <p className="text-sm text-destructive">{formError}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            {t("common:CANCEL")}
          </Button>
          <Button onClick={submit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {t("ADVANCE_RENEWAL_DIALOG.ADVANCE_RENEWAL")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
