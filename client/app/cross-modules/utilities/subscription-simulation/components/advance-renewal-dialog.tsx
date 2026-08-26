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
        title: "Renewal advanced",
        description: paymentOutcome
          ? `Charged as ${paymentOutcome}.`
          : "Charged against the real payment gateway.",
      });

      onOpenChange(false);
    } catch (error) {
      setFormError(error instanceof Error ? error.message : "The renewal could not be advanced.");
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
          <DialogTitle>Advance renewal</DialogTitle>
          <DialogDescription>
            Sends{" "}
            <code>POST /api/subscription-simulation/subscriptions/{subscriptionId}/advance-renewal</code>
            — forces an immediate renewal attempt with a scripted outcome, without waiting for the
            fee schedule&apos;s own due date.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="advance-renewal-outcome">Payment outcome</Label>
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
                {OUTCOMES.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <p className="text-xs text-muted-foreground">
            Advances the one renewal due for the current fee period — there is no simulated clock,
            so this cannot run several future periods in one call.{" "}
            {selection === REAL_GATEWAY
              ? "This will place a real charge against whatever payment provider account is configured (e.g. a Stripe test account)."
              : "Scripting an outcome here never reaches the real payment gateway."}
          </p>

          {formError && <p className="text-sm text-destructive">{formError}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            Cancel
          </Button>
          <Button onClick={submit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Advance renewal
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
