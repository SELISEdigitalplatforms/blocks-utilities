import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { Switch } from "@/components/ui-kits/switch/switch";
import type { SubscriptionPlan } from "../../models/subscription-plan.model";
import { formatPrice } from "../../utilities/subscription-format";
import { eligiblePrices, type CampaignDraft } from "./campaign-draft";

const toggle = (values: string[], value: string) =>
  values.includes(value) ? values.filter((entry) => entry !== value) : [...values, value];

export const StepEligibility = ({
  draft,
  plans,
  onChange,
}: {
  draft: CampaignDraft;
  plans: SubscriptionPlan[];
  onChange: (next: Partial<CampaignDraft>) => void;
}) => {
  const isCampaign = draft.campaignKind !== "Standard";
  const isFreeMonth = draft.campaignKind === "FreeOpeningCalendarPeriod";

  // Restricted to plans that actually offer a price this campaign kind could ever price — an
  // author choosing from anything wider would only find out it cannot be redeemed once Review
  // refuses to submit, or worse, after it is saved.
  const candidatePrices = eligiblePrices(draft.campaignKind, plans);
  const candidatePlanCodes = [...new Set(candidatePrices.map(({ plan }) => plan.code))];
  const selectablePlans = plans.filter(
    (plan) => !isCampaign || candidatePlanCodes.includes(plan.code),
  );
  const selectablePrices = candidatePrices.filter(
    ({ plan }) => draft.planCodes.length === 0 || draft.planCodes.includes(plan.code),
  );

  const planForEntitlement =
    plans.find((plan) => draft.planCodes.includes(plan.code)) ??
    plans.find((plan) =>
      draft.priceIds.some((priceId) => plan.prices.some((price) => price.priceId === priceId)));

  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-lg font-semibold">Eligibility</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          What this discount applies to, and the window it can be redeemed in.
        </p>
      </div>

      <fieldset className="space-y-1">
        <legend className="text-sm font-medium">
          Plans {isCampaign ? "" : "(optional)"}
        </legend>
        <p className="text-xs text-muted-foreground">
          {isCampaign
            ? "Only plans with a price this offer can actually price are listed."
            : "Leave every box clear to allow any plan."}
        </p>
        <div className="max-h-32 space-y-1 overflow-y-auto rounded-md border p-2">
          {selectablePlans.length === 0 && (
            <p className="text-xs text-muted-foreground">
              {isCampaign
                ? "No plan has a price this offer type can be redeemed against yet."
                : "No plans authored yet."}
            </p>
          )}
          {selectablePlans.map((plan) => (
            <label key={plan.planId} className="flex items-center gap-2 text-sm">
              <Checkbox
                checked={draft.planCodes.includes(plan.code)}
                onCheckedChange={() => {
                  const next = toggle(draft.planCodes, plan.code);
                  onChange({
                    planCodes: next,
                    priceIds: draft.priceIds.filter((priceId) =>
                      candidatePrices
                        .filter(({ plan: candidate }) => next.length === 0 || next.includes(candidate.code))
                        .some(({ price }) => price.priceId === priceId)),
                  });
                }}
                aria-label={`Restrict to ${plan.displayName}`}
              />
              <span>
                {plan.displayName} <code className="text-xs">{plan.code}</code>
              </span>
            </label>
          ))}
        </div>
      </fieldset>

      <fieldset className="space-y-1">
        <legend className="text-sm font-medium">
          Prices {isCampaign ? "" : "(optional)"}
        </legend>
        <p className="text-xs text-muted-foreground">
          {isCampaign
            ? "This offer prices a specific price's opening period, not a plan in general — name at least one."
            : "Narrows to one cadence or currency — a yearly-only offer, for instance."}
        </p>
        <div className="max-h-32 space-y-1 overflow-y-auto rounded-md border p-2">
          {selectablePrices.length === 0 && (
            <p className="text-xs text-muted-foreground">No prices to choose from.</p>
          )}
          {selectablePrices.map(({ plan, price }) => (
            <label key={price.priceId} className="flex items-center gap-2 text-sm">
              <Checkbox
                checked={draft.priceIds.includes(price.priceId)}
                onCheckedChange={() => onChange({ priceIds: toggle(draft.priceIds, price.priceId) })}
                aria-label={`Restrict to ${plan.code} ${formatPrice(price)}`}
              />
              <span>
                {plan.code} · {formatPrice(price)}
              </span>
            </label>
          ))}
        </div>
      </fieldset>

      {isCampaign && (
        <>
          <div className="grid gap-5 sm:grid-cols-3">
            <div className="space-y-1.5">
              <Label htmlFor="campaign-from">Starts</Label>
              <Input
                id="campaign-from"
                type="date"
                value={draft.validFromDate}
                onChange={(event) => onChange({ validFromDate: event.target.value })}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="campaign-through">Ends (inclusive)</Label>
              <Input
                id="campaign-through"
                type="date"
                value={draft.validThroughDate}
                onChange={(event) => onChange({ validThroughDate: event.target.value })}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="campaign-timezone">Time zone</Label>
              <Input
                id="campaign-timezone"
                value={draft.timeZoneId}
                onChange={(event) => onChange({ timeZoneId: event.target.value })}
                placeholder="Europe/Zurich"
              />
            </div>
          </div>

          <div className="space-y-3 rounded-md border p-3">
            <div className="flex items-center justify-between gap-4">
              <div>
                <p className="text-sm font-medium">One redemption per organization</p>
                {isFreeMonth && (
                  <p className="text-xs text-muted-foreground">Required for a free-opening-period campaign.</p>
                )}
              </div>
              <Switch
                checked={draft.oneUsePerOrganization}
                onCheckedChange={(checked) => onChange({ oneUsePerOrganization: checked === true })}
                disabled={isFreeMonth}
                aria-label="One redemption per organization"
              />
            </div>
            <div className="flex items-center justify-between gap-4">
              <div>
                <p className="text-sm font-medium">Requires a payment method before activation</p>
                {isFreeMonth && (
                  <p className="text-xs text-muted-foreground">Required for a free-opening-period campaign.</p>
                )}
              </div>
              <Switch
                checked={draft.requiresPaymentMethodUpfront}
                onCheckedChange={(checked) => onChange({ requiresPaymentMethodUpfront: checked === true })}
                disabled={isFreeMonth}
                aria-label="Requires a payment method before activation"
              />
            </div>
          </div>
        </>
      )}

      {isFreeMonth && (
        <div className="grid gap-5 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label htmlFor="campaign-entitlement-key">Temporary entitlement</Label>
            <Input
              id="campaign-entitlement-key"
              list="campaign-entitlement-options"
              value={draft.entitlementKey}
              onChange={(event) => onChange({ entitlementKey: event.target.value })}
              placeholder="seats"
            />
            {planForEntitlement && (
              <datalist id="campaign-entitlement-options">
                {planForEntitlement.entitlements
                  .filter((entitlement) => entitlement.limitKind === "Count")
                  .map((entitlement) => (
                    <option key={entitlement.key} value={entitlement.key} />
                  ))}
              </datalist>
            )}
            <p className="text-xs text-muted-foreground">
              The count entitlement this offer temporarily caps while the free month runs.
            </p>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="campaign-entitlement-limit">Temporary limit</Label>
            <Input
              id="campaign-entitlement-limit"
              type="number"
              min={1}
              value={draft.entitlementLimit}
              onChange={(event) => onChange({ entitlementLimit: event.target.value })}
              placeholder="1"
            />
            <p className="text-xs text-muted-foreground">
              Cannot exceed the plan's own limit for this entitlement.
            </p>
          </div>
        </div>
      )}
    </div>
  );
};
