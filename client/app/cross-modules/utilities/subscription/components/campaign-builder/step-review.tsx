import { useT } from "@seliseblocks/genesis-os";
import type { ReactNode } from "react";
import type { SubscriptionPlan } from "../../models/subscription-plan.model";
import { formatMoney, formatPrice, toMinorUnits } from "../../utilities/subscription-format";
import type { CampaignDraft } from "./campaign-draft";
import { tCampaign } from "./campaign-i18n";

const Row = ({ label, value }: { label: string; value: ReactNode }) => (
  <div className="flex items-baseline justify-between gap-4 py-1.5">
    <span className="text-muted-foreground">{label}</span>
    <span className="text-right font-medium">{value}</span>
  </div>
);

const kindLabel = (kind: CampaignDraft["campaignKind"]): string => {
  switch (kind) {
    case "FreeOpeningCalendarPeriod":
      return tCampaign("STEP_REVIEW.KIND_FREE_OPENING");
    case "FirstAnnualPeriod":
      return tCampaign("STEP_REVIEW.KIND_FIRST_YEAR");
    default:
      return tCampaign("STEP_REVIEW.KIND_STANDARD");
  }
};

const precedenceLabel = (precedence: NonNullable<CampaignDraft["campaignPrecedence"]>): string => {
  switch (precedence) {
    case "ReplaceBuiltIn":
      return tCampaign("STEP_REVIEW.PRECEDENCE_REPLACE");
    case "Stack":
      return tCampaign("STEP_REVIEW.PRECEDENCE_STACK");
    default:
      return tCampaign("STEP_REVIEW.PRECEDENCE_BEST");
  }
};

export const StepReview = ({
  draft,
  plans,
}: {
  draft: CampaignDraft;
  plans: SubscriptionPlan[];
}) => {
  const { t } = useT("utilities.subscription.campaign-builder");
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
        <h2 className="text-lg font-semibold">{t("STEP_REVIEW.TITLE")}</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          {t("STEP_REVIEW.DESCRIPTION")}
        </p>
      </div>

      <div className="divide-y rounded-md border px-3">
        <Row label={t("STEP_REVIEW.CODE")} value={<code>{draft.code || "—"}</code>} />
        <Row label={t("STEP_REVIEW.DISPLAY_NAME")} value={draft.displayName || "—"} />
        <Row label={t("STEP_REVIEW.OFFER_TYPE")} value={kindLabel(draft.campaignKind)} />
        <Row
          label={t("STEP_REVIEW.REDUCTION")}
          value={
            draft.discountKind === "percent"
              ? t("STEP_REVIEW.PERCENT_OFF", { percent: draft.percent || "0" })
              : t("STEP_REVIEW.AMOUNT_OFF", {
                  amount: formatMoney(
                    toMinorUnits(Number(draft.amount || 0), draft.currencyCode),
                    draft.currencyCode,
                  ),
                })
          }
        />
        <Row
          label={t("STEP_REVIEW.PRECEDENCE")}
          value={
            draft.campaignPrecedence
              ? precedenceLabel(draft.campaignPrecedence)
              : t("STEP_REVIEW.PRECEDENCE_LEGACY")
          }
        />
        {!isCampaign && draft.durationPeriods && (
          <Row
            label={t("STEP_REVIEW.DURATION")}
            value={t("STEP_REVIEW.DURATION_PERIODS", {
              count: Number(draft.durationPeriods),
              periods: draft.durationPeriods,
            })}
          />
        )}
        {!isCampaign && draft.startsAtUtc && (
          <Row label={t("STEP_REVIEW.STARTS")} value={new Date(draft.startsAtUtc).toLocaleString()} />
        )}
        {!isCampaign && draft.expiresAtUtc && (
          <Row label={t("STEP_REVIEW.EXPIRES")} value={new Date(draft.expiresAtUtc).toLocaleString()} />
        )}
        <Row
          label={t("STEP_REVIEW.APPLIES_TO")}
          value={
            restrictedPrices || draft.planCodes.length > 0
              ? restrictedPrices || draft.planCodes.join(", ")
              : t("STEP_REVIEW.ANY_PLAN_AND_PRICE")
          }
        />
        {isCampaign && (
          <>
            <Row
              label={t("STEP_REVIEW.WINDOW")}
              value={t("STEP_REVIEW.WINDOW_VALUE", {
                from: draft.validFromDate || "—",
                to: draft.validThroughDate || "—",
                zone: draft.timeZoneId || "—",
              })}
            />
            <Row
              label={t("STEP_REVIEW.ONE_USE")}
              value={draft.oneUsePerOrganization ? t("common:YES") : t("common:NO")}
            />
            <Row
              label={t("STEP_REVIEW.PAYMENT_METHOD_UPFRONT")}
              value={draft.requiresPaymentMethodUpfront ? t("common:YES") : t("common:NO")}
            />
          </>
        )}
        {draft.campaignKind === "FreeOpeningCalendarPeriod" && (
          <Row
            label={t("STEP_REVIEW.TEMP_CAP")}
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
          {t("STEP_REVIEW.SELF_EXPIRING")}
        </p>
      )}
    </div>
  );
};
