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
import { useT } from "@seliseblocks/genesis-os";

export const CancelSubscriptionDialog = ({
  subscription,
  organizationId,
  open,
  onOpenChange,
  onCanceled,
}: {
  subscription: SimulatedSubscription;
  organizationId: string | undefined;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Called with the subscription as the cancellation left it. */
  onCanceled?: (canceled: SimulatedSubscription) => void;
}) => {
  const { t } = useT("utilities.subscription-simulation");
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
        title: t("CANCEL_SUBSCRIPTION_DIALOG.CANCELLATION_SENT"),
        description: immediately
          ? t("CANCEL_SUBSCRIPTION_DIALOG.THE_SUBSCRIPTION_STOPPED_GRANTING")
          : t("CANCEL_SUBSCRIPTION_DIALOG.ACCESS_CONTINUES_UNTIL", { date: new Date(canceled.currentPeriodEndUtc).toLocaleString() }),
      });

      onOpenChange(false);
      onCanceled?.(canceled);
    } catch (error) {
      setFormError(
        error instanceof Error ? error.message : t("CANCEL_SUBSCRIPTION_DIALOG.THE_SUBSCRIPTION_COULD_NOT_BE"),
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
          <DialogTitle>{t("CANCEL_SUBSCRIPTION_DIALOG.CANCEL", { planName: subscription.planName })}</DialogTitle>
          <DialogDescription>
            {t("CANCEL_SUBSCRIPTION_DIALOG.SENDS_EXACTLY", { endpoint: `DELETE /api/subscriptions/${subscription.subscriptionId}` })}
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
                {t("CANCEL_SUBSCRIPTION_DIALOG.CANCEL_AT_PERIOD_END_KEEPS", { date: new Date(subscription.currentPeriodEndUtc).toLocaleDateString() })}</Label>
            </div>
            <div className="flex items-start gap-2">
              <RadioGroupItem value="immediately" id="cancel-immediately" className="mt-1" />
              <Label htmlFor="cancel-immediately" className="font-normal">
                {t("CANCEL_SUBSCRIPTION_DIALOG.CANCEL_IMMEDIATELY_STOPS_GRANTING")}
              </Label>
            </div>
          </RadioGroup>

          <div className="space-y-1.5">
            <Label htmlFor="cancel-reason">{t("CANCEL_SUBSCRIPTION_DIALOG.REASON_OPTIONAL")}</Label>
            <Textarea
              id="cancel-reason"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              placeholder={t("CANCEL_SUBSCRIPTION_DIALOG.RECORDED_ON_THE_CANCELLATION_EVENT")}
            />
          </div>

          {formError && <p className="text-sm text-destructive">{formError}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            {t("common:BACK")}
          </Button>
          <Button variant="destructive" onClick={submit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {t("CANCEL_SUBSCRIPTION_DIALOG.CANCEL_SUBSCRIPTION")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
