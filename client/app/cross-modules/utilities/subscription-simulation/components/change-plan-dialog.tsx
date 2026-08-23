import { Loader2 } from "lucide-react";
import { useMemo, useState } from "react";
import { Badge } from "@/components/ui-kits/badge/badge";
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { toast } from "@/hooks/use-toast";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import { formatPrice } from "../../subscription/utilities/subscription-format";
import { useChangeSubscriptionPlan } from "../hooks/use-change-subscription-plan";
import type {
  SimulatedSubscription,
  SubscriptionQuantity,
} from "../models/subscription-simulation.model";
import { labelPlanChange } from "../utilities/plan-change-label";

export const ChangePlanDialog = ({
  subscription,
  currentPlan,
  plans,
  organizationId,
  open,
  onOpenChange,
}: {
  subscription: SimulatedSubscription;
  currentPlan: SubscriptionPlan | undefined;
  plans: SubscriptionPlan[];
  organizationId: string | undefined;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) => {
  const { mutateAsync, isPending } = useChangeSubscriptionPlan();

  const [targetPlanId, setTargetPlanId] = useState(currentPlan?.planId ?? "");
  const [priceId, setPriceId] = useState("");
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);

  const targetPlan = useMemo(
    () => plans.find((plan) => plan.planId === targetPlanId),
    [plans, targetPlanId],
  );

  const selectedPrice = useMemo(
    () => targetPlan?.prices.find((price) => price.priceId === priceId),
    [targetPlan, priceId],
  );

  const moveLabel = targetPlan ? labelPlanChange(currentPlan, targetPlan) : null;
  const currencyMismatch = Boolean(
    selectedPrice && selectedPrice.currencyCode !== subscription.currencyCode,
  );

  const selectTargetPlan = (planId: string) => {
    setTargetPlanId(planId);
    const plan = plans.find((candidate) => candidate.planId === planId);
    setPriceId(plan?.prices[0]?.priceId ?? "");
    setQuantities(
      Object.fromEntries(
        (plan?.quantityItems ?? []).map((item) => [item.itemKey, String(item.defaultQuantity)]),
      ),
    );
    setFormError(null);
  };

  const submit = async () => {
    setFormError(null);

    if (!targetPlan || !priceId) {
      setFormError("Choose a target plan and price.");
      return;
    }

    if (currencyMismatch) {
      setFormError(
        "This price is in a different currency. A currency change needs a cancel and a fresh subscription, not a plan change.",
      );
      return;
    }

    const parsedQuantities: SubscriptionQuantity[] = [];
    for (const item of targetPlan.quantityItems) {
      const raw = quantities[item.itemKey] ?? "";
      const quantity = Number(raw);

      if (!raw || !Number.isFinite(quantity) || quantity < item.minQuantity) {
        setFormError(`${item.unitLabel} must be at least ${item.minQuantity}.`);
        return;
      }

      if (item.maxQuantity != null && quantity > item.maxQuantity) {
        setFormError(`${item.unitLabel} can be at most ${item.maxQuantity}.`);
        return;
      }

      parsedQuantities.push({ itemKey: item.itemKey, quantity });
    }

    try {
      await mutateAsync({
        subscriptionId: subscription.subscriptionId,
        request: { priceId, quantities: parsedQuantities },
        organizationId,
      });

      toast({
        variant: "success",
        title: `${moveLabel ?? "Plan changed"}`,
        description: `Now on ${targetPlan.displayName}.`,
      });

      onOpenChange(false);
    } catch (error) {
      setFormError(error instanceof Error ? error.message : "The plan could not be changed.");
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
          <DialogTitle>Change plan</DialogTitle>
          <DialogDescription>
            Sends <code>PUT /api/subscriptions/{subscription.subscriptionId}/plan</code> — the
            server follows the chosen price back to its plan and rebuilds billing from there.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="change-plan-target">Target plan</Label>
            <Select value={targetPlanId} onValueChange={selectTargetPlan}>
              <SelectTrigger id="change-plan-target">
                <SelectValue placeholder="Choose a plan" />
              </SelectTrigger>
              <SelectContent>
                {plans.map((plan) => (
                  <SelectItem key={plan.planId} value={plan.planId}>
                    {plan.displayName}
                    {plan.planId === currentPlan?.planId ? " (current)" : ""}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {targetPlan && (
            <div className="space-y-1.5">
              <Label htmlFor="change-plan-price">Target price</Label>
              <Select value={priceId} onValueChange={setPriceId}>
                <SelectTrigger id="change-plan-price">
                  <SelectValue placeholder="Choose a price" />
                </SelectTrigger>
                <SelectContent>
                  {targetPlan.prices.map((price) => (
                    <SelectItem key={price.priceId} value={price.priceId}>
                      {price.displayPriceNote ?? formatPrice(price)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}

          {moveLabel && (
            <div className="flex items-center gap-2">
              <span className="text-xs text-muted-foreground">This is labelled as</span>
              <Badge variant="info" className="font-normal">
                {moveLabel}
              </Badge>
              {currencyMismatch && (
                <Badge variant="error" className="font-normal">
                  Currency mismatch
                </Badge>
              )}
            </div>
          )}

          {targetPlan?.quantityItems.map((item) => (
            <div className="space-y-1.5" key={item.itemKey}>
              <Label htmlFor={`change-quantity-${item.itemKey}`}>{item.unitLabel}</Label>
              <Input
                id={`change-quantity-${item.itemKey}`}
                type="number"
                min={item.minQuantity}
                max={item.maxQuantity ?? undefined}
                value={quantities[item.itemKey] ?? ""}
                onChange={(event) =>
                  setQuantities((current) => ({
                    ...current,
                    [item.itemKey]: event.target.value,
                  }))
                }
              />
            </div>
          ))}

          <p className="text-xs text-muted-foreground">
            The change takes effect immediately and starts a full target billing period — an
            upgrade may charge immediately, a downgrade becomes credit toward future renewals.
          </p>

          {formError && <p className="text-sm text-destructive">{formError}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            Cancel
          </Button>
          <Button onClick={submit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Confirm change
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
