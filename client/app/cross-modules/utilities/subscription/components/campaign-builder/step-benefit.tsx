import { useT } from "@seliseblocks/genesis-os";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { SUBSCRIPTION_CURRENCY_OPTIONS } from "../../constants/subscription.constants";
import { describeDiscountAmountProblem } from "../../utilities/discount-amount";
import {
  exampleMinorAmount,
  minorUnitStep,
} from "../../utilities/subscription-format";
import type { CampaignPrecedence } from "../../models/subscription-plan.model";
import type { CampaignDraft } from "./campaign-draft";

type Translate = ReturnType<typeof useT>["t"];

const precedenceOptions = (
  t: Translate,
): { value: CampaignPrecedence; label: string; description: string }[] => [
  {
    value: "BestDiscount",
    label: t("STEP_BENEFIT.PRECEDENCE_BEST"),
    description: t("STEP_BENEFIT.PRECEDENCE_BEST_DESCRIPTION"),
  },
  {
    value: "ReplaceBuiltIn",
    label: t("STEP_BENEFIT.PRECEDENCE_REPLACE"),
    description: t("STEP_BENEFIT.PRECEDENCE_REPLACE_DESCRIPTION"),
  },
  {
    value: "Stack",
    label: t("STEP_BENEFIT.PRECEDENCE_STACK"),
    description: t("STEP_BENEFIT.PRECEDENCE_STACK_DESCRIPTION"),
  },
];

export const StepBenefit = ({
  draft,
  onChange,
}: {
  draft: CampaignDraft;
  onChange: (next: Partial<CampaignDraft>) => void;
}) => {
  const { t } = useT("utilities.subscription.campaign-builder");
  const isFreeMonth = draft.campaignKind === "FreeOpeningCalendarPeriod";
  const isCampaign = draft.campaignKind !== "Standard";
  const amountMessage =
    draft.discountKind === "fixed" && draft.amount.trim() !== ""
      ? describeDiscountAmountProblem(draft.amount, draft.currencyCode)
      : null;

  const precedences = precedenceOptions(t);

  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-lg font-semibold">{t("STEP_BENEFIT.TITLE")}</h2>
        <p className="mt-1 text-sm text-muted-foreground">{t("STEP_BENEFIT.DESCRIPTION")}</p>
      </div>

      {isFreeMonth ? (
        <p className="rounded-md border border-blocks-primary-200 bg-blocks-primary-50 p-3 text-sm text-blocks-primary-900">
          {t("STEP_BENEFIT.FREE_MONTH_NOTE")}
        </p>
      ) : (
        <div className="grid gap-5 sm:grid-cols-3">
          <div className="space-y-1.5">
            <Label>{t("STEP_BENEFIT.KIND")}</Label>
            <Select
              value={draft.discountKind}
              onValueChange={(value) => onChange({ discountKind: value as "percent" | "fixed" })}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="percent">{t("STEP_BENEFIT.KIND_PERCENT")}</SelectItem>
                <SelectItem value="fixed">{t("STEP_BENEFIT.KIND_FIXED")}</SelectItem>
              </SelectContent>
            </Select>
          </div>

          {draft.discountKind === "percent" ? (
            <div className="space-y-1.5">
              <Label htmlFor="campaign-percent">{t("STEP_BENEFIT.PERCENT_OFF")}</Label>
              <Input
                id="campaign-percent"
                type="number"
                min={0.01}
                max={100}
                value={draft.percent}
                onChange={(event) => onChange({ percent: event.target.value })}
              />
            </div>
          ) : (
            <div className="grid grid-cols-2 gap-2">
              <div>
                <Label htmlFor="campaign-amount">
                  {t("STEP_BENEFIT.AMOUNT_OFF", { currency: draft.currencyCode })}
                </Label>
                <Input
                  id="campaign-amount"
                  type="number"
                  min={0}
                  step={minorUnitStep(draft.currencyCode)}
                  placeholder={exampleMinorAmount(draft.currencyCode)}
                  value={draft.amount}
                  onChange={(event) => onChange({ amount: event.target.value })}
                  aria-invalid={amountMessage !== null}
                  aria-describedby={amountMessage ? "campaign-amount-problem" : undefined}
                />
                {amountMessage && (
                  <p id="campaign-amount-problem" className="mt-1 text-xs text-destructive">
                    {amountMessage}
                  </p>
                )}
              </div>
              <div>
                <Label htmlFor="campaign-currency">{t("STEP_BENEFIT.CURRENCY")}</Label>
                <Select value={draft.currencyCode} onValueChange={(value) => onChange({ currencyCode: value })}>
                  <SelectTrigger id="campaign-currency">
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
            </div>
          )}
        </div>
      )}

      <div className="space-y-2">
        <Label>{t("STEP_BENEFIT.PRECEDENCE_LABEL")}</Label>
        <Select
          value={draft.campaignPrecedence}
          onValueChange={(value) => onChange({ campaignPrecedence: value as CampaignPrecedence })}
        >
          <SelectTrigger aria-label={t("STEP_BENEFIT.PRECEDENCE_ARIA")}>
            <SelectValue placeholder={t("STEP_BENEFIT.PRECEDENCE_PLACEHOLDER")} />
          </SelectTrigger>
          <SelectContent>
            {precedences.map((option) => (
              <SelectItem key={option.value} value={option.value}>
                {option.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <p className="text-xs text-muted-foreground">
          {draft.campaignPrecedence
            ? precedences.find((option) => option.value === draft.campaignPrecedence)?.description
            : t("STEP_BENEFIT.PRECEDENCE_LEGACY")}
        </p>
      </div>

      {!isCampaign ? (
        <div className="space-y-5">
          {/* A credit purchase is one charge, so there is no later period for a duration to reach. */}
          {draft.scope !== "CreditPurchase" && (
            <>
          {/* On its own row. Sharing one with a date was what made this field read as part of the
              availability window, when it counts charges rather than bounding a period of time. */}
          <div className="space-y-1.5">
            <Label htmlFor="campaign-duration">
              {t("STEP_BENEFIT.DURATION")}
            </Label>
            <Input
              id="campaign-duration"
              type="number"
              min={1}
              value={draft.durationPeriods}
              onChange={(event) => onChange({ durationPeriods: event.target.value })}
              aria-describedby="campaign-duration-help"
            />
            <p id="campaign-duration-help" className="text-xs text-muted-foreground">
              {t("STEP_BENEFIT.DURATION_HINT")}
            </p>
          </div>
            </>
          )}
          {/* The two availability dates are one decision, so they sit as one pair. They stack
              below sm, where a datetime-local control has no room to share a row. */}
          <div className="grid gap-5 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="campaign-start">{t("STEP_BENEFIT.STARTS_AT")}</Label>
              <Input
                id="campaign-start"
                type="datetime-local"
                value={draft.startsAtUtc}
                onChange={(event) => onChange({ startsAtUtc: event.target.value })}
              />
              <p className="text-xs text-muted-foreground">{t("STEP_BENEFIT.STARTS_AT_HINT")}</p>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="campaign-expiry">{t("STEP_BENEFIT.EXPIRES_AT")}</Label>
              <Input
                id="campaign-expiry"
                type="datetime-local"
                value={draft.expiresAtUtc}
                onChange={(event) => onChange({ expiresAtUtc: event.target.value })}
              />
              <p className="text-xs text-muted-foreground">{t("STEP_BENEFIT.EXPIRES_AT_HINT")}</p>
            </div>
          </div>
        </div>
      ) : null}

      {isCampaign && !isFreeMonth && (
        <p className="text-xs text-muted-foreground">
          {t("STEP_BENEFIT.SELF_EXPIRING")}
        </p>
      )}
    </div>
  );
};
