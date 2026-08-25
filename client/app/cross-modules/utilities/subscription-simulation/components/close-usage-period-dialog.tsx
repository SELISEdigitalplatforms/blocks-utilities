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

/** Not a server value — selecting this omits `paymentOutcome` so the real gateway decides. */
const REAL_GATEWAY = "RealGateway" as const;

const OUTCOMES: { value: SimulatedRenewalOutcome | typeof REAL_GATEWAY; label: string }[] = [
  { value: REAL_GATEWAY, label: "Real payment gateway (no script)" },
  { value: "Succeeded", label: "Succeeded" },
  { value: "Declined", label: "Declined" },
  { value: "InsufficientFunds", label: "Insufficient funds" },
  { value: "PaymentMethodExpired", label: "Payment method expired" },
  { value: "ProviderUnavailable", label: "Provider unavailable" },
  { value: "OutcomeUnknown", label: "Outcome unknown" },
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
        title: "Usage period closed",
        description: !chargeInvoice
          ? "No overage invoice was charged."
          : paymentOutcome
            ? `Any overage invoice was charged as ${paymentOutcome}.`
            : "Any overage invoice was charged against the real payment gateway.",
      });

      onOpenChange(false);
    } catch (error) {
      setFormError(
        error instanceof Error ? error.message : "The usage period could not be closed.",
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
          <DialogTitle>Close usage period</DialogTitle>
          <DialogDescription>
            Sends{" "}
            <code>
              POST /api/subscription-simulation/subscriptions/{subscriptionId}/close-usage-period
            </code>
            — closes the current usage period now, prices any overage, and — unless told
            otherwise — charges it with a scripted outcome.
          </DialogDescription>
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
              Also charge the overage invoice this close produces, if any.
            </Label>
          </div>

          {chargeInvoice && (
            <div className="space-y-1.5">
              <Label htmlFor="close-usage-period-outcome">Payment outcome</Label>
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
                  {OUTCOMES.map((option) => (
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
            Cancel
          </Button>
          <Button onClick={submit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Close period
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
