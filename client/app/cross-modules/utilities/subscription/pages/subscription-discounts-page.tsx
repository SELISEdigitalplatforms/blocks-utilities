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
import { SUBSCRIPTION_CURRENCY_OPTIONS } from "../constants/subscription.constants";
import { describeDiscountAmountProblem } from "../utilities/discount-amount";
import {
  exampleMinorAmount,
  formatMoney,
  formatPrice,
  minorUnitStep,
  toMinorUnits,
} from "../utilities/subscription-format";

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
  const [amount, setAmount] = useState("");
  const [currencyCode, setCurrencyCode] = useState("USD");
  const [durationPeriods, setDurationPeriods] = useState("");
  const [expiresAtUtc, setExpiresAtUtc] = useState("");
  const [planCodes, setPlanCodes] = useState<string[]>([]);
  const [priceIds, setPriceIds] = useState<string[]>([]);

  // Null when the amount is sendable. Only asked of a fixed discount: a percentage has no
  // currency and no amount to be wrong about.
  const amountProblem =
    kind === "fixed" ? describeDiscountAmountProblem(amount, currencyCode) : null;

  // Shown only once there is something to correct. An empty field is already visibly empty, and
  // colouring it red before anybody has typed reads as a mistake they have not made yet — while
  // the button stays disabled either way.
  const amountMessage = amount.trim() === "" ? null : amountProblem;

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
      // The only place this figure changes units. The field holds what the author typed; the API
      // has always wanted minor units and still does.
      amountMinor: kind === "fixed" ? toMinorUnits(Number(amount), currencyCode) : undefined,
      currencyCode: kind === "fixed" ? currencyCode : undefined,
      durationPeriods: durationPeriods ? Number(durationPeriods) : undefined,
      expiresAtUtc: expiresAtUtc ? new Date(expiresAtUtc).toISOString() : undefined,
      applicablePlanCodes: planCodes,
      // Both lists narrow, so an empty one is "unrestricted by that" rather than "matches nothing".
      applicablePriceIds: priceIds,
    }),
    onSuccess: async () => {
      setCode(""); setDisplayName(""); setAmount(""); setPlanCodes([]); setPriceIds([]);
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
          {kind === "percent" ? <div><Label htmlFor="discount-percent">Percent off</Label><Input id="discount-percent" type="number" min={0.01} max={100} value={percent} onChange={(event) => setPercent(event.target.value)} /></div> : <div className="grid grid-cols-2 gap-2">
              <div>
                <Label htmlFor="discount-amount">Amount off ({currencyCode})</Label>
                <Input
                  id="discount-amount"
                  type="number"
                  min={0}
                  step={minorUnitStep(currencyCode)}
                  placeholder={exampleMinorAmount(currencyCode)}
                  value={amount}
                  onChange={(event) => setAmount(event.target.value)}
                  aria-invalid={amountMessage !== null}
                  aria-describedby={amountMessage ? "discount-amount-problem" : undefined}
                />
                {/* Named here rather than left to the disabled button, which says that something
                    is wrong without saying what. */}
                {amountMessage && (
                  <p id="discount-amount-problem" className="mt-1 text-xs text-destructive">
                    {amountMessage}
                  </p>
                )}
              </div>
              <div>
                <Label htmlFor="discount-currency">Currency</Label>
                {/* A list rather than three free characters. The exponent decides how many
                    decimals the amount may carry, and a currency nothing knows the exponent of
                    would be assumed to have two — wrong for yen, and wrong by a factor of ten
                    for dinars. */}
                <Select value={currencyCode} onValueChange={setCurrencyCode}>
                  <SelectTrigger id="discount-currency">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {SUBSCRIPTION_CURRENCY_OPTIONS.map((currency) => (
                      <SelectItem key={currency.code} value={currency.code}>
                        {currency.code} — {currency.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>}
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
        <Button disabled={!code.trim() || !displayName.trim() || amountProblem !== null || create.isPending} onClick={() => create.mutate()}>{create.isPending ? "Creating…" : "Create discount"}</Button>
      </Card>
      <Card className="rounded-xl p-0">
        <div className="border-b p-4 font-semibold">Discount catalogue</div>
        <div className="divide-y">
          {(discounts.data ?? []).map((discount) => (
            <div key={discount.discountId} className="flex items-center justify-between gap-4 p-4">
              <div><p className="font-medium">{discount.displayName} <code className="text-xs">{discount.code}</code></p><p className="text-xs text-muted-foreground">{discount.percentBasisPoints ? `${discount.percentBasisPoints / 100}% off` : `${formatMoney(discount.amountMinor ?? 0, discount.currencyCode ?? "USD")} off`} · {discount.status}</p><p className="text-xs text-muted-foreground">{describeApplicability(discount.applicablePlanCodes, discount.applicablePriceIds, availablePlans)}</p></div>
              {discount.status === "Active" && <Button variant="ghost" size="sm" onClick={() => archive.mutate(discount.discountId)}>Retire</Button>}
            </div>
          ))}
          {!discounts.isLoading && !(discounts.data?.length) && <p className="p-4 text-sm text-muted-foreground">No discounts authored yet.</p>}
        </div>
      </Card>
    </main>
  );
};
