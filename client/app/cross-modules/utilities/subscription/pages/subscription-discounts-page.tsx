import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams } from "react-router";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui-kits/select/select";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { useOrganizationScope } from "../hooks/use-organization-scope";
import { subscriptionService } from "../services/subscription.service";

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
  const [plans, setPlans] = useState("");

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
      applicablePlanCodes: plans.split(",").map((value) => value.trim()).filter(Boolean),
    }),
    onSuccess: async () => {
      setCode(""); setDisplayName(""); setPlans("");
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
          <div><Label htmlFor="discount-plans">Plan codes (optional)</Label><Input id="discount-plans" value={plans} onChange={(event) => setPlans(event.target.value)} placeholder="pro, max-5x" /></div>
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
              <div><p className="font-medium">{discount.displayName} <code className="text-xs">{discount.code}</code></p><p className="text-xs text-muted-foreground">{discount.percentBasisPoints ? `${discount.percentBasisPoints / 100}% off` : `${discount.amountMinor} ${discount.currencyCode} minor units off`} · {discount.status}</p></div>
              {discount.status === "Active" && <Button variant="ghost" size="sm" onClick={() => archive.mutate(discount.discountId)}>Retire</Button>}
            </div>
          ))}
          {!discounts.isLoading && !(discounts.data?.length) && <p className="p-4 text-sm text-muted-foreground">No discounts authored yet.</p>}
        </div>
      </Card>
    </main>
  );
};
