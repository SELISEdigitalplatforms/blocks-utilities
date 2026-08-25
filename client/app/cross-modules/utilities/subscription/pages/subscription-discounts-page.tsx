import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams } from "react-router";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui-kits/select/select";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { useOrganizationScope } from "../hooks/use-organization-scope";
import { useSubscriptionPlans } from "../hooks/use-subscription-plans";
import type { SubscriptionPlan } from "../models/subscription-plan.model";
import { subscriptionService } from "../services/subscription.service";
import { formatPrice } from "../utilities/subscription-format";

/**
 * What a discount applies to, in one line.
 *
 * Names the cadence rather than the identifier: "pro · CHF 145.00 / year" is what somebody authored,
 * and a price id tells a reader nothing about whether the restriction is the one they meant. A price
 * that has since been retired is still shown by id, because the restriction is still in force.
 */
const describeApplicability = (
  planCodes: string[],
  priceIds: string[] | undefined,
  plans: SubscriptionPlan[],
): string => {
  const prices = (priceIds ?? []).map((priceId) => {
    const match = plans
      .flatMap((plan) => plan.prices.map((price) => ({ plan, price })))
      .find((candidate) => candidate.price.priceId === priceId);

    return match ? `${match.plan.code} · ${formatPrice(match.price)}` : priceId;
  });

  if (planCodes.length === 0 && prices.length === 0) {
    return "Applies to any plan and any price.";
  }

  const parts = [
    planCodes.length > 0 ? `plans: ${planCodes.join(", ")}` : null,
    prices.length > 0 ? `prices: ${prices.join(", ")}` : null,
  ].filter(Boolean);

  // "and" rather than "or": both restrictions have to match, which is the part an author is most
  // likely to get wrong when they set both.
  return `Restricted to ${parts.join(" and ")}.`;
};

export const SubscriptionDiscountsPage = () => {
  const { itemId } = useParams();
  const organizationId = useOrganizationScope();
  const queryClient = useQueryClient();
  const [code, setCode] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [percent, setPercent] = useState("10");
  const [kind, setKind] = useState<"percent" | "fixed">("percent");
  const [amountMinor, setAmountMinor] = useState("");
  const [currencyCode, setCurrencyCode] = useState("USD");
  const [durationPeriods, setDurationPeriods] = useState("");
  const [expiresAtUtc, setExpiresAtUtc] = useState("");
  const [planCodes, setPlanCodes] = useState<string[]>([]);
  const [priceIds, setPriceIds] = useState<string[]>([]);

  const catalogue = useSubscriptionPlans(organizationId);
  const availablePlans = catalogue.data ?? [];

  // Only the prices of the plans that are actually restricted to. A discount aimed at one plan and
  // a price belonging to another can never match, and offering that pair invites authoring it.
  const selectablePrices = availablePlans
    .filter((plan) => planCodes.length === 0 || planCodes.includes(plan.code))
    .flatMap((plan) => plan.prices.map((price) => ({ plan, price })));

  const toggle = (values: string[], value: string) =>
    values.includes(value) ? values.filter((entry) => entry !== value) : [...values, value];

  const discounts = useQuery({
    queryKey: ["subscription-discounts", organizationId],
    queryFn: () => subscriptionService.listDiscounts(organizationId),
  });
  const create = useMutation({
    mutationFn: () => subscriptionService.createDiscount({
      organizationId,
      code: code.trim(),
      displayName: displayName.trim(),
      kind: kind === "percent" ? 0 : 1,
      percentBasisPoints: kind === "percent" ? Math.round(Number(percent) * 100) : undefined,
      amountMinor: kind === "fixed" ? Number(amountMinor) : undefined,
      currencyCode: kind === "fixed" ? currencyCode : undefined,
      durationPeriods: durationPeriods ? Number(durationPeriods) : undefined,
      expiresAtUtc: expiresAtUtc ? new Date(expiresAtUtc).toISOString() : undefined,
      applicablePlanCodes: planCodes,
      // Both lists narrow, so an empty one is "unrestricted by that" rather than "matches nothing".
      applicablePriceIds: priceIds,
    }),
    onSuccess: async () => {
      setCode(""); setDisplayName(""); setPlanCodes([]); setPriceIds([]);
      await queryClient.invalidateQueries({ queryKey: ["subscription-discounts"] });
    },
  });
  const archive = useMutation({
    mutationFn: (discountId: string) => subscriptionService.archiveDiscount(discountId, organizationId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["subscription-discounts"] }),
  });

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SubscriptionPlanPageHeader
        title="Subscription discounts"
        description="Reusable discount codes are validated at signup and copied onto each subscription."
        backTo={`/app/${itemId ?? ""}/subscription/plans`}
      />
      <Card className="space-y-4 rounded-xl">
        <h2 className="font-semibold">Create discount</h2>
        <div className="grid gap-3 sm:grid-cols-2">
          <div><Label htmlFor="discount-code">Code</Label><Input id="discount-code" value={code} onChange={(event) => setCode(event.target.value)} placeholder="launch25" /></div>
          <div><Label htmlFor="discount-name">Display name</Label><Input id="discount-name" value={displayName} onChange={(event) => setDisplayName(event.target.value)} placeholder="Launch offer" /></div>
          <div><Label>Kind</Label><Select value={kind} onValueChange={(value) => setKind(value as "percent" | "fixed")}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="percent">Percentage</SelectItem><SelectItem value="fixed">Fixed amount</SelectItem></SelectContent></Select></div>
          {kind === "percent" ? <div><Label htmlFor="discount-percent">Percent off</Label><Input id="discount-percent" type="number" min={0.01} max={100} value={percent} onChange={(event) => setPercent(event.target.value)} /></div> : <div className="grid grid-cols-2 gap-2"><div><Label htmlFor="discount-amount">Minor units off</Label><Input id="discount-amount" type="number" min={1} value={amountMinor} onChange={(event) => setAmountMinor(event.target.value)} /></div><div><Label htmlFor="discount-currency">Currency</Label><Input id="discount-currency" maxLength={3} value={currencyCode} onChange={(event) => setCurrencyCode(event.target.value.toUpperCase())} /></div></div>}
          <fieldset className="space-y-1">
            <legend className="text-sm font-medium">Plans (optional)</legend>
            <p className="text-xs text-muted-foreground">Leave every box clear to allow any plan.</p>
            <div className="max-h-32 space-y-1 overflow-y-auto rounded-md border p-2">
              {availablePlans.length === 0 && (
                <p className="text-xs text-muted-foreground">No plans authored yet.</p>
              )}
              {availablePlans.map((plan) => (
                <label key={plan.planId} className="flex items-center gap-2 text-sm">
                  <Checkbox
                    checked={planCodes.includes(plan.code)}
                    onCheckedChange={() => {
                      const next = toggle(planCodes, plan.code);
                      setPlanCodes(next);
                      // Prices belonging to a plan that is no longer selected cannot match anything,
                      // so they are dropped rather than left in a state nobody can see or undo.
                      setPriceIds((current) =>
                        current.filter((priceId) =>
                          availablePlans
                            .filter((candidate) =>
                              next.length === 0 || next.includes(candidate.code))
                            .some((candidate) =>
                              candidate.prices.some((price) => price.priceId === priceId)),
                        ),
                      );
                    }}
                    aria-label={`Restrict to ${plan.displayName}`}
                  />
                  <span>{plan.displayName} <code className="text-xs">{plan.code}</code></span>
                </label>
              ))}
            </div>
          </fieldset>
          <fieldset className="space-y-1">
            <legend className="text-sm font-medium">Prices (optional)</legend>
            <p className="text-xs text-muted-foreground">
              Narrows to one cadence or currency — a yearly-only offer, for instance.
            </p>
            <div className="max-h-32 space-y-1 overflow-y-auto rounded-md border p-2">
              {selectablePrices.length === 0 && (
                <p className="text-xs text-muted-foreground">No prices to choose from.</p>
              )}
              {selectablePrices.map(({ plan, price }) => (
                <label key={price.priceId} className="flex items-center gap-2 text-sm">
                  <Checkbox
                    checked={priceIds.includes(price.priceId)}
                    onCheckedChange={() => setPriceIds(toggle(priceIds, price.priceId))}
                    aria-label={`Restrict to ${plan.code} ${formatPrice(price)}`}
                  />
                  <span>{plan.code} · {formatPrice(price)}</span>
                </label>
              ))}
            </div>
          </fieldset>
          <div><Label htmlFor="discount-duration">Duration in billing periods (optional)</Label><Input id="discount-duration" type="number" min={1} value={durationPeriods} onChange={(event) => setDurationPeriods(event.target.value)} /></div>
          <div><Label htmlFor="discount-expiry">Expires at (optional)</Label><Input id="discount-expiry" type="datetime-local" value={expiresAtUtc} onChange={(event) => setExpiresAtUtc(event.target.value)} /></div>
        </div>
        {create.error && <p className="text-sm text-destructive">{create.error.message}</p>}
        <Button disabled={!code.trim() || !displayName.trim() || create.isPending} onClick={() => create.mutate()}>{create.isPending ? "Creating…" : "Create discount"}</Button>
      </Card>
      <Card className="rounded-xl p-0">
        <div className="border-b p-4 font-semibold">Discount catalogue</div>
        <div className="divide-y">
          {(discounts.data ?? []).map((discount) => (
            <div key={discount.discountId} className="flex items-center justify-between gap-4 p-4">
              <div><p className="font-medium">{discount.displayName} <code className="text-xs">{discount.code}</code></p><p className="text-xs text-muted-foreground">{discount.percentBasisPoints ? `${discount.percentBasisPoints / 100}% off` : `${discount.amountMinor} ${discount.currencyCode} minor units off`} · {discount.status}</p><p className="text-xs text-muted-foreground">{describeApplicability(discount.applicablePlanCodes, discount.applicablePriceIds, availablePlans)}</p></div>
              {discount.status === "Active" && <Button variant="ghost" size="sm" onClick={() => archive.mutate(discount.discountId)}>Retire</Button>}
            </div>
          ))}
          {!discounts.isLoading && !(discounts.data?.length) && <p className="p-4 text-sm text-muted-foreground">No discounts authored yet.</p>}
        </div>
      </Card>
    </main>
  );
};
