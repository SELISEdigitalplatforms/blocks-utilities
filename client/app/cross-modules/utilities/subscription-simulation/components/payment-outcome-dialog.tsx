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

const PURPOSES: { value: SubscriptionPaymentPurpose; label: string }[] = [
  { value: "InitialCharge", label: "Initial charge" },
  { value: "Renewal", label: "Renewal" },
];

const OUTCOMES: { value: SimulatedRenewalOutcome; label: string }[] = [
  { value: "Succeeded", label: "Succeeded" },
  { value: "Declined", label: "Declined" },
  { value: "InsufficientFunds", label: "Insufficient funds" },
  { value: "PaymentMethodExpired", label: "Payment method expired" },
  { value: "ProviderUnavailable", label: "Provider unavailable" },
  { value: "OutcomeUnknown", label: "Outcome unknown" },
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
        title: outcome === "Succeeded" ? "Payment marked succeeded" : "Payment marked failed",
        description: `${paymentPurpose} settled as ${outcome}.`,
      });

      onOpenChange(false);
    } catch (error) {
      setFormError(
        error instanceof Error ? error.message : "The payment outcome could not be simulated.",
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
          <DialogTitle>Simulate payment outcome</DialogTitle>
          <DialogDescription>
            Sends{" "}
            <code>
              POST /api/subscription-simulation/subscriptions/{subscriptionId}/mark-payment-
              {outcome === "Succeeded" ? "succeeded" : "failed"}
            </code>
            — settles the subscription&apos;s outstanding charge through the same path a real
            provider confirmation would take.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="payment-outcome-purpose">Charge</Label>
            <Select
              value={paymentPurpose}
              onValueChange={(value) => setPaymentPurpose(value as SubscriptionPaymentPurpose)}
            >
              <SelectTrigger id="payment-outcome-purpose">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {PURPOSES.map((purpose) => (
                  <SelectItem key={purpose.value} value={purpose.value}>
                    {purpose.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="payment-outcome-outcome">Outcome</Label>
            <Select
              value={outcome}
              onValueChange={(value) => setOutcome(value as SimulatedRenewalOutcome)}
            >
              <SelectTrigger id="payment-outcome-outcome">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {OUTCOMES.map((option) => (
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
                Provider reference (optional)
              </Label>
              <Input
                id="payment-outcome-provider-reference"
                value={providerReference}
                onChange={(event) => setProviderReference(event.target.value)}
                placeholder="Generated when left blank"
              />
            </div>
          ) : (
            <div className="space-y-1.5">
              <Label htmlFor="payment-outcome-error-code">Error code (optional)</Label>
              <Input
                id="payment-outcome-error-code"
                value={errorCode}
                onChange={(event) => setErrorCode(event.target.value)}
                placeholder="Overrides the outcome's default error code"
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
              Also run the real settlement processor (activation or renewal). Leave unchecked to
              only record the outcome and inspect the intermediate state.
            </Label>
          </div>

          {formError && <p className="text-sm text-destructive">{formError}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            Cancel
          </Button>
          <Button onClick={submit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Simulate
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
