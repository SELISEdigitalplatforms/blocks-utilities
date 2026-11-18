import { useT } from "@seliseblocks/genesis-os";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui-kits/radio-group/radio-group";
import type { CampaignKind, DiscountScope } from "../../models/subscription-plan.model";
import type { CampaignDraft } from "./campaign-draft";

type Translate = ReturnType<typeof useT>["t"];

const kindOptions = (t: Translate): { value: CampaignKind; title: string; description: string }[] => [
  {
    value: "Standard",
    title: t("STEP_IDENTITY.KIND_STANDARD"),
    description: t("STEP_IDENTITY.KIND_STANDARD_DESCRIPTION"),
  },
  {
    value: "FreeOpeningCalendarPeriod",
    title: t("STEP_IDENTITY.KIND_FREE_OPENING"),
    description: t("STEP_IDENTITY.KIND_FREE_OPENING_DESCRIPTION"),
  },
  {
    value: "FirstAnnualPeriod",
    title: t("STEP_IDENTITY.KIND_FIRST_YEAR"),
    description: t("STEP_IDENTITY.KIND_FIRST_YEAR_DESCRIPTION"),
  },
];

const scopeOptions = (t: Translate): { value: DiscountScope; title: string; description: string }[] => [
  {
    value: "Subscription",
    title: t("STEP_IDENTITY.SCOPE_SUBSCRIPTIONS"),
    description: t("STEP_IDENTITY.SCOPE_SUBSCRIPTIONS_DESCRIPTION"),
  },
  {
    value: "CreditPurchase",
    title: t("STEP_IDENTITY.SCOPE_CREDITS"),
    description: t("STEP_IDENTITY.SCOPE_CREDITS_DESCRIPTION"),
  },
];

export const StepIdentity = ({
  draft,
  onChange,
  codeReadOnly = false,
}: {
  draft: CampaignDraft;
  onChange: (next: Partial<CampaignDraft>) => void;
  codeReadOnly?: boolean;
}) => {
  const { t } = useT("utilities.subscription.campaign-builder");

  return (
  <div className="space-y-5">
    <div>
      <h2 className="text-lg font-semibold">{t("STEP_IDENTITY.TITLE")}</h2>
      <p className="mt-1 text-sm text-muted-foreground">
        {t("STEP_IDENTITY.DESCRIPTION")}
      </p>
    </div>

    <div className="grid gap-5 sm:grid-cols-2">
      <div className="space-y-1.5">
        <Label htmlFor="campaign-code">{t("STEP_IDENTITY.CODE")}</Label>
        <Input
          id="campaign-code"
          value={draft.code}
          onChange={(event) => onChange({ code: event.target.value })}
          // eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose
          placeholder="launch25"
          autoComplete="off"
          spellCheck={false}
          readOnly={codeReadOnly}
        />
        <p className="text-xs text-muted-foreground">
          {t("STEP_IDENTITY.CODE_HINT")}
        </p>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="campaign-name">{t("STEP_IDENTITY.DISPLAY_NAME")}</Label>
        <Input
          id="campaign-name"
          value={draft.displayName}
          onChange={(event) => onChange({ displayName: event.target.value })}
          placeholder={t("STEP_IDENTITY.DISPLAY_NAME_PLACEHOLDER")}
        />
      </div>
    </div>

    <div className="space-y-2">
      <Label>{t("STEP_IDENTITY.APPLIES_TO")}</Label>
      <RadioGroup
        value={draft.scope}
        onValueChange={(value) => onChange({ scope: value as DiscountScope })}
        className="gap-3 sm:grid-cols-2"
      >
        {scopeOptions(t).map((option) => (
          <label
            key={option.value}
            htmlFor={`campaign-scope-${option.value}`}
            className="flex cursor-pointer items-start gap-3 rounded-lg border p-3 text-sm has-[:checked]:border-blocks-primary-400 has-[:checked]:bg-blocks-primary-50"
          >
            <RadioGroupItem
              value={option.value}
              id={`campaign-scope-${option.value}`}
              className="mt-0.5"
            />
            <span>
              <span className="font-medium">{option.title}</span>
              <span className="mt-0.5 block text-xs text-muted-foreground">
                {option.description}
              </span>
            </span>
          </label>
        ))}
      </RadioGroup>
    </div>

    {/* Every campaign kind prices a subscription period, so a credit code has none to choose. */}
    {draft.scope === "Subscription" && (
    <div className="space-y-2">
      <Label>{t("STEP_IDENTITY.OFFER_TYPE")}</Label>
      <RadioGroup
        value={draft.campaignKind}
        onValueChange={(value) => onChange({ campaignKind: value as CampaignKind })}
        className="gap-3"
      >
        {kindOptions(t).map((option) => (
          <label
            key={option.value}
            htmlFor={`campaign-kind-${option.value}`}
            className="flex cursor-pointer items-start gap-3 rounded-lg border p-3 text-sm has-[:checked]:border-blocks-primary-400 has-[:checked]:bg-blocks-primary-50"
          >
            <RadioGroupItem
              value={option.value}
              id={`campaign-kind-${option.value}`}
              className="mt-0.5"
            />
            <span>
              <span className="font-medium">{option.title}</span>
              <span className="mt-0.5 block text-xs text-muted-foreground">
                {option.description}
              </span>
            </span>
          </label>
        ))}
      </RadioGroup>
    </div>
    )}
  </div>
  );
};
