import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui-kits/radio-group/radio-group";
import type { CampaignKind } from "../../models/subscription-plan.model";
import type { CampaignDraft } from "./campaign-draft";

const KIND_OPTIONS: { value: CampaignKind; title: string; description: string }[] = [
  {
    value: "Standard",
    title: "Standard discount",
    description: "An ordinary percentage or fixed reduction. No date window, no redemption limit.",
  },
  {
    value: "FreeOpeningCalendarPeriod",
    title: "Free opening month",
    description:
      "100% off a calendar-aligned monthly price's opening period only — signup to the next " +
      "calendar-month boundary — paired with a temporary entitlement cap. One redemption per " +
      "organization, and requires a card upfront.",
  },
  {
    value: "FirstAnnualPeriod",
    title: "First-year discount",
    description:
      "Discounts a calendar-aligned yearly price's first full annual period only. A renewal past " +
      "that reverts to the price's own rate automatically — no expiry date to manage.",
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
}) => (
  <div className="space-y-5">
    <div>
      <h2 className="text-lg font-semibold">Identity</h2>
      <p className="mt-1 text-sm text-muted-foreground">
        What this discount is called, and what kind of offer it is.
      </p>
    </div>

    <div className="grid gap-5 sm:grid-cols-2">
      <div className="space-y-1.5">
        <Label htmlFor="campaign-code">Code</Label>
        <Input
          id="campaign-code"
          value={draft.code}
          onChange={(event) => onChange({ code: event.target.value })}
          placeholder="launch25"
          autoComplete="off"
          spellCheck={false}
          readOnly={codeReadOnly}
        />
        <p className="text-xs text-muted-foreground">
          Lowercase letters, digits, hyphens and underscores only. Fixed once created.
        </p>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="campaign-name">Display name</Label>
        <Input
          id="campaign-name"
          value={draft.displayName}
          onChange={(event) => onChange({ displayName: event.target.value })}
          placeholder="Launch offer"
        />
      </div>
    </div>

    <div className="space-y-2">
      <Label>Offer type</Label>
      <RadioGroup
        value={draft.campaignKind}
        onValueChange={(value) => onChange({ campaignKind: value as CampaignKind })}
        className="gap-3"
      >
        {KIND_OPTIONS.map((option) => (
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
  </div>
);
