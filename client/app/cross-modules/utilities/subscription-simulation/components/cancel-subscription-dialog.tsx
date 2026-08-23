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
import { RadioGroup, RadioGroupItem } from "@/components/ui-kits/radio-group/radio-group";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { toast } from "@/hooks/use-toast";
import { useCancelSubscription } from "../hooks/use-cancel-subscription";
import type { SimulatedSubscription } from "../models/subscription-simulation.model";

export const CancelSubscriptionDialog = ({
  subscription,
  organizationId,
  open,
  onOpenChange,
}: {
  subscription: SimulatedSubscription;
  organizationId: string | undefined;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) => {
  const { mutateAsync, isPending } = useCancelSubscription();

  const [immediately, setImmediately] = useState(false);
  const [reason, setReason] = useState("");
  const [formError, setFormError] = useState<string | null>(null);

  const submit = async () => {
    setFormError(null);

    try {
      const canceled = await mutateAsync({
        subscriptionId: subscription.subscriptionId,
        immediately,
        reason: reason.trim() || undefined,
        organizationId,
      });

      toast({
        variant: "success",
        title: "Cancellation sent",
        description: immediately
          ? "The subscription stopped granting immediately."
          : `Access continues until ${new Date(canceled.currentPeriodEndUtc).toLocaleString()}.`,
      });

      onOpenChange(false);
    } catch (error) {
      setFormError(
        error instanceof Error ? error.message : "The subscription could not be canceled.",
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
          <DialogTitle>Cancel {subscription.planName}</DialogTitle>
          <DialogDescription>
            Sends <code>DELETE /api/subscriptions/{subscription.subscriptionId}</code> exactly as
            an integrating application would.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <RadioGroup
            value={immediately ? "immediately" : "period-end"}
            onValueChange={(value) => setImmediately(value === "immediately")}
          >
            <div className="flex items-start gap-2">
              <RadioGroupItem value="period-end" id="cancel-period-end" className="mt-1" />
              <Label htmlFor="cancel-period-end" className="font-normal">
                Cancel at period end — keeps granting until{" "}
                {new Date(subscription.currentPeriodEndUtc).toLocaleDateString()}, then stops
                renewing.
              </Label>
            </div>
            <div className="flex items-start gap-2">
              <RadioGroupItem value="immediately" id="cancel-immediately" className="mt-1" />
              <Label htmlFor="cancel-immediately" className="font-normal">
                Cancel immediately — stops granting right now.
              </Label>
            </div>
          </RadioGroup>

          <div className="space-y-1.5">
            <Label htmlFor="cancel-reason">Reason (optional)</Label>
            <Textarea
              id="cancel-reason"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              placeholder="Recorded on the cancellation event."
            />
          </div>

          {formError && <p className="text-sm text-destructive">{formError}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            Back
          </Button>
          <Button variant="destructive" onClick={submit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Cancel subscription
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
