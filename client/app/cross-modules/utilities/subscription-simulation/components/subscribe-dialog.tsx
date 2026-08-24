import { Loader2 } from "lucide-react";
import { useMemo, useState } from "react";
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
import { detectBrowserTimeZone } from "../constants/subscription-simulation.constants";
import { useSubscribeToPlan } from "../hooks/use-subscribe-to-plan";
import type { SubscriptionQuantity } from "../models/subscription-simulation.model";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import { formatPrice } from "../../subscription/utilities/subscription-format";

export const SubscribeDialog = ({
  plan,
  organizationId,
  open,
  onOpenChange,
  onSubscribed,
}: {
  plan: SubscriptionPlan;
  organizationId: string | undefined;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSubscribed: (checkoutUrl: string | null) => void;
}) => {
  const { mutateAsync, isPending } = useSubscribeToPlan();

  const [priceId, setPriceId] = useState(plan.prices[0]?.priceId ?? "");
  const [quantities, setQuantities] = useState<Record<string, string>>(() =>
    Object.fromEntries(
      plan.quantityItems.map((item) => [item.itemKey, String(item.defaultQuantity)]),
    ),
  );
  const [discountCode, setDiscountCode] = useState("");
  const [billingEmail, setBillingEmail] = useState("");
  const [billingName, setBillingName] = useState("");
  const [formError, setFormError] = useState<string | null>(null);

  const selectedPrice = useMemo(
    () => plan.prices.find((price) => price.priceId === priceId),
    [plan.prices, priceId],
  );

  const submit = async () => {
    setFormError(null);

    if (!priceId) {
      setFormError("Choose a price to subscribe on.");
      return;
    }

    const parsedQuantities: SubscriptionQuantity[] = [];
    for (const item of plan.quantityItems) {
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
      const subscription = await mutateAsync({
        planCode: plan.code,
        priceId,
        quantities: parsedQuantities,
        timeZoneId: detectBrowserTimeZone(),
        discountCode: discountCode.trim() || undefined,
        billingEmail: billingEmail.trim() || undefined,
        billingName: billingName.trim() || undefined,
        organizationId,
      });

      toast({
        variant: "success",
        title: "Subscription started",
        description: subscription.checkoutUrl
          ? "Continue to checkout to activate it."
          : `Status: ${subscription.status}.`,
      });

      onOpenChange(false);
      onSubscribed(subscription.checkoutUrl);
    } catch (error) {
      setFormError(
        error instanceof Error ? error.message : "The subscription could not be started.",
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
          <DialogTitle>Subscribe to {plan.displayName}</DialogTitle>
          <DialogDescription>
            Sends <code>POST /api/subscriptions</code> exactly as an integrating application
            would.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="subscribe-price">Price</Label>
            <Select value={priceId} onValueChange={setPriceId}>
              <SelectTrigger id="subscribe-price">
                <SelectValue placeholder="Choose a price" />
              </SelectTrigger>
              <SelectContent>
                {plan.prices.map((price) => (
                  <SelectItem key={price.priceId} value={price.priceId}>
                    {price.displayPriceNote ?? formatPrice(price)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {plan.quantityItems.map((item) => (
            <div className="space-y-1.5" key={item.itemKey}>
              <Label htmlFor={`quantity-${item.itemKey}`}>
                {item.unitLabel}
                {selectedPrice?.quantityItemKey === item.itemKey && " (price multiplier)"}
              </Label>
              <Input
                id={`quantity-${item.itemKey}`}
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

          <div className="space-y-1.5">
            <Label htmlFor="subscribe-discount">Discount code (optional)</Label>
            <Input
              id="subscribe-discount"
              value={discountCode}
              onChange={(event) => setDiscountCode(event.target.value)}
              placeholder="e.g. LAUNCH20"
            />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="subscribe-email">Billing email (optional)</Label>
              <Input
                id="subscribe-email"
                type="email"
                value={billingEmail}
                onChange={(event) => setBillingEmail(event.target.value)}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="subscribe-name">Billing name (optional)</Label>
              <Input
                id="subscribe-name"
                value={billingName}
                onChange={(event) => setBillingName(event.target.value)}
              />
            </div>
          </div>

          {formError && <p className="text-sm text-destructive">{formError}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            Cancel
          </Button>
          <Button onClick={submit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Subscribe
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
