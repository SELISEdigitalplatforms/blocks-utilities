import { useT } from "@seliseblocks/genesis-os";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { Switch } from "@/components/ui-kits/switch/switch";
import type { SubscriptionPlan } from "../../models/subscription-plan.model";
import { formatPrice } from "../../utilities/subscription-format";
import { eligiblePrices, purchasableMeters, type CampaignDraft } from "./campaign-draft";

const STEP_ELIGIBILITY_ONE_REDEMPTION = "STEP_ELIGIBILITY.ONE_REDEMPTION";

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
  const { t } = useT("utilities.subscription.campaign-builder");
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
        <h2 className="text-lg font-semibold">{t("STEP_ELIGIBILITY.TITLE")}</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          {t("STEP_ELIGIBILITY.DESCRIPTION")}
        </p>
      </div>

      <fieldset className="space-y-1">
        <legend className="text-sm font-medium">
          {isCampaign ? t("STEP_ELIGIBILITY.PLANS") : t("STEP_ELIGIBILITY.PLANS_OPTIONAL")}
        </legend>
        <p className="text-xs text-muted-foreground">
          {isCampaign
            ? t("STEP_ELIGIBILITY.PLANS_CAMPAIGN_HINT")
            : t("STEP_ELIGIBILITY.PLANS_HINT")}
        </p>
        <div className="max-h-32 space-y-1 overflow-y-auto rounded-md border p-2">
          {selectablePlans.length === 0 && (
            <p className="text-xs text-muted-foreground">
              {isCampaign
                ? t("STEP_ELIGIBILITY.PLANS_NONE_CAMPAIGN")
                : t("STEP_ELIGIBILITY.PLANS_NONE")}
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
                aria-label={t("STEP_ELIGIBILITY.RESTRICT_TO_PLAN", { name: plan.displayName })}
              />
              <span>
                {plan.displayName} <code className="text-xs">{plan.code}</code>
              </span>
            </label>
          ))}
        </div>
      </fieldset>

      {draft.scope === "CreditPurchase" && (
        <CreditEligibility draft={draft} plans={plans} onChange={onChange} />
      )}

      {draft.scope === "Subscription" && (
      <fieldset className="space-y-1">
        <legend className="text-sm font-medium">
          {isCampaign ? t("STEP_ELIGIBILITY.PRICES") : t("STEP_ELIGIBILITY.PRICES_OPTIONAL")}
        </legend>
        <p className="text-xs text-muted-foreground">
          {isCampaign
            ? t("STEP_ELIGIBILITY.PRICES_CAMPAIGN_HINT")
            : t("STEP_ELIGIBILITY.PRICES_HINT")}
        </p>
        <div className="max-h-32 space-y-1 overflow-y-auto rounded-md border p-2">
          {selectablePrices.length === 0 && (
            <p className="text-xs text-muted-foreground">{t("STEP_ELIGIBILITY.PRICES_NONE")}</p>
          )}
          {selectablePrices.map(({ plan, price }) => (
            <label key={price.priceId} className="flex items-center gap-2 text-sm">
              <Checkbox
                checked={draft.priceIds.includes(price.priceId)}
                onCheckedChange={() => onChange({ priceIds: toggle(draft.priceIds, price.priceId) })}
                aria-label={t("STEP_ELIGIBILITY.RESTRICT_TO_PRICE", {
                  plan: plan.code,
                  price: formatPrice(price),
                })}
              />
              <span>
                {plan.code} · {formatPrice(price)}
              </span>
            </label>
          ))}
        </div>
      </fieldset>
      )}

      {isCampaign && (
        <>
          <div className="grid gap-5 sm:grid-cols-3">
            <div className="space-y-1.5">
              <Label htmlFor="campaign-from">{t("STEP_ELIGIBILITY.STARTS")}</Label>
              <Input
                id="campaign-from"
                type="date"
                value={draft.validFromDate}
                onChange={(event) => onChange({ validFromDate: event.target.value })}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="campaign-through">
                {isFreeMonth ? t("STEP_ELIGIBILITY.ENDS_OPTIONAL") : t("STEP_ELIGIBILITY.ENDS")}
              </Label>
              <Input
                id="campaign-through"
                type="date"
                value={draft.validThroughDate}
                onChange={(event) => onChange({ validThroughDate: event.target.value })}
              />
              {isFreeMonth && (
                <p className="text-xs text-muted-foreground">
                  {t("STEP_ELIGIBILITY.ENDS_HINT")}
                </p>
              )}
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="campaign-timezone">{t("STEP_ELIGIBILITY.TIME_ZONE")}</Label>
              <Input
                id="campaign-timezone"
                value={draft.timeZoneId}
                onChange={(event) => onChange({ timeZoneId: event.target.value })}
                // eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose
                placeholder="Europe/Zurich"
              />
            </div>
          </div>

          <div className="space-y-3 rounded-md border p-3">
            <div className="flex items-center justify-between gap-4">
              <div>
                <p className="text-sm font-medium">{t(STEP_ELIGIBILITY_ONE_REDEMPTION)}</p>
                {isFreeMonth && (
                  <p className="text-xs text-muted-foreground">{t("STEP_ELIGIBILITY.REQUIRED_FOR_FREE")}</p>
                )}
              </div>
              <Switch
                checked={draft.oneUsePerOrganization}
                onCheckedChange={(checked) => onChange({ oneUsePerOrganization: checked === true })}
                disabled={isFreeMonth}
                aria-label={t(STEP_ELIGIBILITY_ONE_REDEMPTION)}
              />
            </div>
            <div className="flex items-center justify-between gap-4">
              <div>
                <p className="text-sm font-medium">{t("STEP_ELIGIBILITY.REQUIRES_PAYMENT_METHOD")}</p>
                {isFreeMonth && (
                  <p className="text-xs text-muted-foreground">{t("STEP_ELIGIBILITY.REQUIRED_FOR_FREE")}</p>
                )}
              </div>
              <Switch
                checked={draft.requiresPaymentMethodUpfront}
                onCheckedChange={(checked) => onChange({ requiresPaymentMethodUpfront: checked === true })}
                disabled={isFreeMonth}
                aria-label={t("STEP_ELIGIBILITY.REQUIRES_PAYMENT_METHOD")}
              />
            </div>
          </div>
        </>
      )}

      {isFreeMonth && (
        <div className="grid gap-5 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label htmlFor="campaign-entitlement-key">{t("STEP_ELIGIBILITY.TEMP_ENTITLEMENT")}</Label>
            <Input
              id="campaign-entitlement-key"
              list="campaign-entitlement-options"
              value={draft.entitlementKey}
              onChange={(event) => onChange({ entitlementKey: event.target.value })}
              // eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose
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
              {t("STEP_ELIGIBILITY.TEMP_ENTITLEMENT_HINT")}
            </p>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="campaign-entitlement-limit">{t("STEP_ELIGIBILITY.TEMP_LIMIT")}</Label>
            <Input
              id="campaign-entitlement-limit"
              type="number"
              min={1}
              value={draft.entitlementLimit}
              onChange={(event) => onChange({ entitlementLimit: event.target.value })}
              placeholder="1"
            />
            <p className="text-xs text-muted-foreground">
              {t("STEP_ELIGIBILITY.TEMP_LIMIT_HINT")}
            </p>
          </div>
        </div>
      )}
    </div>
  );
};

/**
 * What a credit-purchase code is limited to beyond its plans: the meters it may be used on, and
 * whether each organization may use it once. Prices are not offered, because a credit purchase is
 * not charged against a subscription price and a code limited to one could never be redeemed.
 */
const CreditEligibility = ({
  draft,
  plans,
  onChange,
}: {
  draft: CampaignDraft;
  plans: SubscriptionPlan[];
  onChange: (next: Partial<CampaignDraft>) => void;
}) => {
  const { t } = useT("utilities.subscription.campaign-builder");
  const meters = purchasableMeters(draft, plans);

  return (
    <>
      <fieldset className="space-y-1">
        <legend className="text-sm font-medium">{t("STEP_ELIGIBILITY.METERS_OPTIONAL")}</legend>
        <p className="text-xs text-muted-foreground">
          {t("STEP_ELIGIBILITY.METERS_HINT")}
        </p>
        <div className="max-h-32 space-y-1 overflow-y-auto rounded-md border p-2">
          {meters.length === 0 && (
            <p className="text-xs text-muted-foreground">
              {t("STEP_ELIGIBILITY.METERS_NONE")}
            </p>
          )}
          {meters.map((meter) => (
            <label key={meter.meterKey} className="flex items-center gap-2 text-sm">
              <Checkbox
                checked={draft.meterKeys.includes(meter.meterKey)}
                onCheckedChange={() => onChange({ meterKeys: toggle(draft.meterKeys, meter.meterKey) })}
                aria-label={t("STEP_ELIGIBILITY.RESTRICT_TO_METER", { meter: meter.label })}
              />
              <span>{meter.label}</span>
            </label>
          ))}
        </div>
      </fieldset>

      <div className="flex items-center justify-between gap-4 rounded-md border p-3">
        <div>
          <p className="text-sm font-medium">{t(STEP_ELIGIBILITY_ONE_REDEMPTION)}</p>
          <p className="text-xs text-muted-foreground">
            {t("STEP_ELIGIBILITY.ONE_REDEMPTION_CREDIT_HINT")}
          </p>
        </div>
        <Switch
          checked={draft.oneUsePerOrganization}
          onCheckedChange={(checked) => onChange({ oneUsePerOrganization: checked === true })}
          aria-label={t(STEP_ELIGIBILITY_ONE_REDEMPTION)}
        />
      </div>
    </>
  );
};
