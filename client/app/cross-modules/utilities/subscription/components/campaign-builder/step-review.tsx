import type { ReactNode } from "react";
import type { SubscriptionPlan } from "../../models/subscription-plan.model";
import { formatMoney, formatPrice, toMinorUnits } from "../../utilities/subscription-format";
import type { CampaignDraft } from "./campaign-draft";

const Row = ({ label, value }: { label: string; value: ReactNode }) => (
  <div className="flex items-baseline justify-between gap-4 py-1.5">
    <span className="text-muted-foreground">{label}</span>
    <span className="text-right font-medium">{value}</span>
  </div>
);

const KIND_LABEL: Record<CampaignDraft["campaignKind"], string> = {
  Standard: "Standard discount",
  FreeOpeningCalendarPeriod: "Free opening month",
  FirstAnnualPeriod: "First-year discount",
};

export const StepReview = ({
  draft,
  plans,
}: {
  draft: CampaignDraft;
  plans: SubscriptionPlan[];
}) => {
  const restrictedPrices = draft.priceIds
    .map((priceId) => {
      const match = plans
        .flatMap((plan) => plan.prices.map((price) => ({ plan, price })))
        .find((candidate) => candidate.price.priceId === priceId);
      return match ? `${match.plan.code} · ${formatPrice(match.price)}` : priceId;
    })
    .join(", ");

  const isCampaign = draft.campaignKind !== "Standard";

  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-lg font-semibold">Review</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Check everything before creating this discount — the code cannot be changed afterward.
        </p>
      </div>

      <div className="divide-y rounded-md border px-3">
        <Row label="Code" value={<code>{draft.code || "—"}</code>} />
        <Row label="Display name" value={draft.displayName || "—"} />
        <Row label="Offer type" value={KIND_LABEL[draft.campaignKind]} />
        <Row
          label="Reduction"
          value={
            draft.discountKind === "percent"
              ? `${draft.percent || "0"}% off`
              : `${formatMoney(toMinorUnits(Number(draft.amount || 0), draft.currencyCode), draft.currencyCode)} off`
          }
        />
        {isCampaign && (
          <Row
            label="Meets the price's own discount"
            value={
              { BestDiscount: "Best discount wins", ReplaceBuiltIn: "Replaces it", Stack: "Stacks on top" }[
                draft.campaignPrecedence
              ]
            }
          />
        )}
        {!isCampaign && draft.durationPeriods && (
          <Row label="Duration" value={`${draft.durationPeriods} billing periods`} />
        )}
        {!isCampaign && draft.startsAtUtc && (
          <Row label="Starts" value={new Date(draft.startsAtUtc).toLocaleString()} />
        )}
        {!isCampaign && draft.expiresAtUtc && (
          <Row label="Expires" value={new Date(draft.expiresAtUtc).toLocaleString()} />
        )}
        <Row
          label="Applies to"
          value={
            restrictedPrices || draft.planCodes.length > 0
              ? restrictedPrices || draft.planCodes.join(", ")
              : "Any plan and price"
          }
        />
        {isCampaign && (
          <>
            <Row
              label="Redeemable window"
              value={`${draft.validFromDate || "—"} to ${draft.validThroughDate || "—"} (${
                draft.timeZoneId || "—"
              })`}
            />
            <Row label="One use per organization" value={draft.oneUsePerOrganization ? "Yes" : "No"} />
            <Row
              label="Requires payment method upfront"
              value={draft.requiresPaymentMethodUpfront ? "Yes" : "No"}
            />
          </>
        )}
        {draft.campaignKind === "FreeOpeningCalendarPeriod" && (
          <Row
            label="Temporary entitlement cap"
            value={
              draft.entitlementKey
                ? `${draft.entitlementKey} → ${draft.entitlementLimit || "—"}`
                : "—"
            }
          />
        )}
      </div>

      {isCampaign && (
        <p className="rounded-md border border-blocks-primary-200 bg-blocks-primary-50 p-3 text-xs text-blocks-primary-900">
          This offer expires and reverts to standard pricing on its own — there is nothing to
          schedule or remember to turn off.
        </p>
      )}
    </div>
  );
};
