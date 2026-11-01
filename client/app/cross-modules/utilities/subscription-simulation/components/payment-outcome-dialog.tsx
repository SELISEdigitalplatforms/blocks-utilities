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
import { useMarkPaymentFailed } from "../hooks/use-mark-payment-failed";
import { useMarkPaymentSucceeded } from "../hooks/use-mark-payment-succeeded";
import type {
  SimulatedRenewalOutcome,
  SubscriptionPaymentPurpose,
  SubscriptionSimulationActionResponse,
} from "../models/subscription-simulation-harness.model";
import { useT, tStatic } from "@seliseblocks/genesis-os";

const getPurposes = (): { value: SubscriptionPaymentPurpose; label: string }[] => [
  { value: "InitialCharge", label: tStatic("utilities.subscription-simulation", "PAYMENT_OUTCOME_DIALOG.INITIAL_CHARGE") },
  { value: "Renewal", label: tStatic("utilities.subscription-simulation", "PAYMENT_OUTCOME_DIALOG.RENEWAL") },
];

const getOutcomes = (): { value: SimulatedRenewalOutcome; label: string }[] => [
  { value: "Succeeded", label: tStatic("utilities.subscription-simulation", "PAYMENT_OUTCOME_DIALOG.SUCCEEDED") },
  { value: "Declined", label: tStatic("utilities.subscription-simulation", "PAYMENT_OUTCOME_DIALOG.DECLINED") },
  { value: "InsufficientFunds", label: tStatic("utilities.subscription-simulation", "PAYMENT_OUTCOME_DIALOG.INSUFFICIENT_FUNDS") },
  { value: "PaymentMethodExpired", label: tStatic("utilities.subscription-simulation", "PAYMENT_OUTCOME_DIALOG.PAYMENT_METHOD_EXPIRED") },
  { value: "ProviderUnavailable", label: tStatic("utilities.subscription-simulation", "PAYMENT_OUTCOME_DIALOG.PROVIDER_UNAVAILABLE") },
  { value: "OutcomeUnknown", label: tStatic("utilities.subscription-simulation", "PAYMENT_OUTCOME_DIALOG.OUTCOME_UNKNOWN") },
];

export const PaymentOutcomeDialog = ({
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
  const succeeded = useMarkPaymentSucceeded();
  const failed = useMarkPaymentFailed();
  const isPending = succeeded.isPending || failed.isPending;

  const [paymentPurpose, setPaymentPurpose] = useState<SubscriptionPaymentPurpose>("Renewal");
  const [outcome, setOutcome] = useState<SimulatedRenewalOutcome>("Succeeded");
  const [providerReference, setProviderReference] = useState("");
  const [errorCode, setErrorCode] = useState("");
  const [runProcessor, setRunProcessor] = useState(true);
  const [formError, setFormError] = useState<string | null>(null);

  const submit = async () => {
    setFormError(null);

    try {
      const result =
        outcome === "Succeeded"
          ? await succeeded.mutateAsync({
              subscriptionId,
              request: {
                organizationId,
                paymentPurpose,
                providerReference: providerReference.trim() || undefined,
                runProcessor,
              },
            })
          : await failed.mutateAsync({
              subscriptionId,
              request: {
                organizationId,
                paymentPurpose,
                failureKind: outcome,
                errorCode: errorCode.trim() || undefined,
                runProcessor,
              },
            });

      onResult(result);

      toast({
        variant: outcome === "Succeeded" ? "success" : "destructive",
        title: outcome === "Succeeded" ? t("PAYMENT_OUTCOME_DIALOG.PAYMENT_MARKED_SUCCEEDED") : t("PAYMENT_OUTCOME_DIALOG.PAYMENT_MARKED_FAILED"),
        description: t("PAYMENT_OUTCOME_DIALOG.SETTLED_AS", { paymentPurpose, outcome }),
      });

      onOpenChange(false);
    } catch (error) {
      setFormError(
        error instanceof Error ? error.message : t("PAYMENT_OUTCOME_DIALOG.THE_PAYMENT_OUTCOME_COULD_NOT"),
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
          <DialogTitle>{t("PAYMENT_OUTCOME_DIALOG.SIMULATE_PAYMENT_OUTCOME")}</DialogTitle>
          <DialogDescription>
            {t("PAYMENT_OUTCOME_DIALOG.SENDS_SETTLES_THE_SUBSCRIPTION_S", { endpoint: `POST /api/subscription-simulation/subscriptions/${subscriptionId}/mark-payment-${outcome === "Succeeded" ? "succeeded" : "failed"}` })}</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="payment-outcome-purpose">{t("PAYMENT_OUTCOME_DIALOG.CHARGE")}</Label>
            <Select
              value={paymentPurpose}
              onValueChange={(value) => setPaymentPurpose(value as SubscriptionPaymentPurpose)}
            >
              <SelectTrigger id="payment-outcome-purpose">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {getPurposes().map((purpose) => (
                  <SelectItem key={purpose.value} value={purpose.value}>
                    {purpose.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="payment-outcome-outcome">{t("PAYMENT_OUTCOME_DIALOG.OUTCOME")}</Label>
            <Select
              value={outcome}
              onValueChange={(value) => setOutcome(value as SimulatedRenewalOutcome)}
            >
              <SelectTrigger id="payment-outcome-outcome">
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

          {outcome === "Succeeded" ? (
            <div className="space-y-1.5">
              <Label htmlFor="payment-outcome-provider-reference">
                {t("PAYMENT_OUTCOME_DIALOG.PROVIDER_REFERENCE_OPTIONAL")}
              </Label>
              <Input
                id="payment-outcome-provider-reference"
                value={providerReference}
                onChange={(event) => setProviderReference(event.target.value)}
                placeholder={t("PAYMENT_OUTCOME_DIALOG.GENERATED_WHEN_LEFT_BLANK")}
              />
            </div>
          ) : (
            <div className="space-y-1.5">
              <Label htmlFor="payment-outcome-error-code">{t("PAYMENT_OUTCOME_DIALOG.ERROR_CODE_OPTIONAL")}</Label>
              <Input
                id="payment-outcome-error-code"
                value={errorCode}
                onChange={(event) => setErrorCode(event.target.value)}
                placeholder={t("PAYMENT_OUTCOME_DIALOG.OVERRIDES_THE_OUTCOME_S_DEFAULT")}
              />
            </div>
          )}

          <div className="flex items-start gap-2">
            <Checkbox
              id="payment-outcome-run-processor"
              checked={runProcessor}
              onCheckedChange={(checked) => setRunProcessor(checked === true)}
              className="mt-0.5"
            />
            <Label htmlFor="payment-outcome-run-processor" className="font-normal">
              {t("PAYMENT_OUTCOME_DIALOG.ALSO_RUN_THE_REAL_SETTLEMENT")}
            </Label>
          </div>

          {formError && <p className="text-sm text-destructive">{formError}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            {t("common:CANCEL")}
          </Button>
          <Button onClick={submit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {t("PAYMENT_OUTCOME_DIALOG.SIMULATE")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
