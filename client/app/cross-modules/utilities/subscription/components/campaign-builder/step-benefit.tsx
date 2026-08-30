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

const PRECEDENCE_OPTIONS: { value: CampaignPrecedence; label: string; description: string }[] = [
  {
    value: "BestDiscount",
    label: "Best discount wins",
    description: "Whichever reduction is larger — this campaign's, or the price's own — never both.",
  },
  {
    value: "ReplaceBuiltIn",
    label: "Replace the price's own discount",
    description: "This campaign's reduction only, for as long as it applies — even if smaller.",
  },
  {
    value: "Stack",
    label: "Stack on top",
    description: "Both reductions combined, capped at 100% of the price.",
  },
];

export const StepBenefit = ({
  draft,
  onChange,
}: {
  draft: CampaignDraft;
  onChange: (next: Partial<CampaignDraft>) => void;
}) => {
  const isFreeMonth = draft.campaignKind === "FreeOpeningCalendarPeriod";
  const isCampaign = draft.campaignKind !== "Standard";
  const amountMessage =
    draft.discountKind === "fixed" && draft.amount.trim() !== ""
      ? describeDiscountAmountProblem(draft.amount, draft.currencyCode)
      : null;

  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-lg font-semibold">Benefit</h2>
        <p className="mt-1 text-sm text-muted-foreground">What redeeming this discount takes off.</p>
      </div>

      {isFreeMonth ? (
        <p className="rounded-md border border-blocks-primary-200 bg-blocks-primary-50 p-3 text-sm text-blocks-primary-900">
          A free opening month is always a full 100% reduction — nothing to set here.
        </p>
      ) : (
        <div className="grid gap-5 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label>Kind</Label>
            <Select
              value={draft.discountKind}
              onValueChange={(value) => onChange({ discountKind: value as "percent" | "fixed" })}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="percent">Percentage</SelectItem>
                <SelectItem value="fixed">Fixed amount</SelectItem>
              </SelectContent>
            </Select>
          </div>

          {draft.discountKind === "percent" ? (
            <div className="space-y-1.5">
              <Label htmlFor="campaign-percent">Percent off</Label>
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
                <Label htmlFor="campaign-amount">Amount off ({draft.currencyCode})</Label>
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
                <Label htmlFor="campaign-currency">Currency</Label>
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

      {isCampaign ? (
        <div className="space-y-2">
          <Label>How it meets the price's own discount</Label>
          <Select
            value={draft.campaignPrecedence}
            onValueChange={(value) => onChange({ campaignPrecedence: value as CampaignPrecedence })}
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {PRECEDENCE_OPTIONS.map((option) => (
                <SelectItem key={option.value} value={option.value}>
                  {option.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <p className="text-xs text-muted-foreground">
            {PRECEDENCE_OPTIONS.find((option) => option.value === draft.campaignPrecedence)?.description}
          </p>
        </div>
      ) : (
        <div className="grid gap-5 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label htmlFor="campaign-duration">Duration in billing periods (optional)</Label>
            <Input
              id="campaign-duration"
              type="number"
              min={1}
              value={draft.durationPeriods}
              onChange={(event) => onChange({ durationPeriods: event.target.value })}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="campaign-expiry">Expires at (optional)</Label>
            <Input
              id="campaign-expiry"
              type="datetime-local"
              value={draft.expiresAtUtc}
              onChange={(event) => onChange({ expiresAtUtc: event.target.value })}
            />
          </div>
        </div>
      )}

      {isCampaign && !isFreeMonth && (
        <p className="text-xs text-muted-foreground">
          This offer expires itself — a first-year discount stops after the first annual period,
          automatically. There is no duration to set.
        </p>
      )}
    </div>
  );
};
