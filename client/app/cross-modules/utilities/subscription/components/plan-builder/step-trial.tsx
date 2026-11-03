import { useT } from "@seliseblocks/genesis-os";
import { useFieldArray, useFormContext, useWatch } from "react-hook-form";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import { stepFor } from "../../utilities/meter-quantity";
import { CardListItem, CardListShell } from "./card-list-shell";
import { StepHeading } from "./step-heading";

/** Not a server value — selecting this clears the trial fields entirely. */
const NO_TRIAL = "None" as const;

type Translate = ReturnType<typeof useT>["t"];

const trialDurationKindOptions = (
  t: Translate,
): {
  value: "Days" | "EndOfCalendarMonth" | "AnniversaryMonths";
  label: string;
  description: string;
}[] => [
  {
    value: "Days",
    label: t("STEP_TRIAL.KIND_DAYS"),
    description: t("STEP_TRIAL.KIND_DAYS_DESCRIPTION"),
  },
  {
    value: "EndOfCalendarMonth",
    label: t("STEP_TRIAL.KIND_END_OF_MONTH"),
    description: t("STEP_TRIAL.KIND_END_OF_MONTH_DESCRIPTION"),
  },
  {
    value: "AnniversaryMonths",
    label: t("STEP_TRIAL.KIND_MONTHS"),
    description: t("STEP_TRIAL.KIND_MONTHS_DESCRIPTION"),
  },
];

export const StepTrial = () => {
  const { t } = useT("utilities.subscription.plan-builder");
  const kindOptions = trialDurationKindOptions(t);
  const { control, setValue } = useFormContext<CreateSubscriptionPlanFormValues>();
  const trialGrants = useFieldArray({ control, name: "trialGrants" });
  const trialDurationKind = useWatch({ control, name: "trialDurationKind" });
  const trialRequiresPaymentMethod = useWatch({
    control,
    name: "trialRequiresPaymentMethod",
  });
  const oneTrialPerPlan = useWatch({ control, name: "oneTrialPerPlan" });
  const meters = useWatch({ control, name: "meters" });
  const grantValues = useWatch({ control, name: "trialGrants" });

  // Zero — whole units — until a meter is chosen, matching how the server reads a meter that
  // declared no scale.
  const scaleOfNamedMeter = (index: number) =>
    (meters ?? []).find((meter) => meter.meterKey === grantValues?.[index]?.meterKey)
      ?.quantityScale ?? 0;

  return (
    <div className="space-y-6">
      <StepHeading
        eyebrow={t("STEP_TRIAL.TITLE")}
        title={t("STEP_TRIAL.TITLE")}
        description={t("STEP_TRIAL.DESCRIPTION")}
      />

      <div className="grid gap-5 sm:grid-cols-2">
        <FormField
          control={control}
          name="trialDurationKind"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("STEP_TRIAL.DURATION")}</FormLabel>
              <Select
                value={field.value ?? NO_TRIAL}
                onValueChange={(value) => {
                  if (value === NO_TRIAL) {
                    field.onChange(undefined);
                    setValue("trialDurationCount", undefined);
                    return;
                  }
                  field.onChange(value);
                  // Every kind uses a different valid range (or none at all), so a count left
                  // over from a different kind would otherwise fail silently until submit.
                  setValue("trialDurationCount", undefined);
                }}
              >
                <FormControl>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                </FormControl>
                <SelectContent>
                  <SelectItem value={NO_TRIAL}>{t("STEP_TRIAL.NO_TRIAL")}</SelectItem>
                  {kindOptions.map((option) => (
                    <SelectItem key={option.value} value={option.value}>
                      {option.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                {trialDurationKind
                  ? kindOptions.find((option) => option.value === trialDurationKind)
                      ?.description
                  : t("STEP_TRIAL.NO_TRIAL_HINT")}
              </p>
              <FormMessage />
            </FormItem>
          )}
        />

        {trialDurationKind && trialDurationKind !== "EndOfCalendarMonth" && (
          <FormField
            control={control}
            name="trialDurationCount"
            render={({ field }) => (
              <FormItem>
                <FormLabel>
                  {trialDurationKind === "AnniversaryMonths"
                    ? t("STEP_TRIAL.MONTHS")
                    : t("STEP_TRIAL.DAYS")}
                </FormLabel>
                <FormControl>
                  <Input
                    {...field}
                    value={field.value ?? ""}
                    type="number"
                    min={1}
                    max={trialDurationKind === "AnniversaryMonths" ? 12 : 365}
                  />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
        )}

        <FormField
          control={control}
          name="trialRequiresPaymentMethod"
          render={({ field }) => (
            <FormItem className="flex flex-col justify-end">
              <div className="flex items-center gap-2">
                <FormControl>
                  <Checkbox checked={field.value} onCheckedChange={field.onChange} />
                </FormControl>
                <FormLabel className="!m-0">{t("STEP_TRIAL.REQUIRE_CARD")}</FormLabel>
              </div>
              {/*
                Both branches describe a trial that is free until it ends. Requiring a card and
                charging for the first period used to be the same act, because the money path could
                not hold a card without taking money with it, and this copy said so. Card setup
                separated them: the card is stored by a setup session that charges nothing.
              */}
              <p className="text-xs text-muted-foreground">
                {trialRequiresPaymentMethod
                  ? t("STEP_TRIAL.CARD_HINT")
                  : t("STEP_TRIAL.NO_CARD_HINT")}
              </p>
            </FormItem>
          )}
        />

        <FormField
          control={control}
          name="oneTrialPerPlan"
          render={({ field }) => (
            <FormItem className="flex flex-col justify-end">
              <div className="flex items-center gap-2">
                <FormControl>
                  <Checkbox checked={field.value} onCheckedChange={field.onChange} />
                </FormControl>
                <FormLabel className="!m-0">One trial per plan</FormLabel>
              </div>
              <p className="text-xs text-muted-foreground">
                {oneTrialPerPlan
                  ? "Each organization gets the trial once, whoever buys it. Subscribing again after cancelling goes to paid checkout."
                  : "Every signup gets the trial, including an organization that cancelled and comes back."}
              </p>
            </FormItem>
          )}
        />
      </div>

      {Boolean(trialDurationKind) && (
        <div className="space-y-3">
          <h3 className="text-sm font-semibold">{t("STEP_TRIAL.GRANTS")}</h3>
          <p className="text-xs text-muted-foreground">
            {t("STEP_TRIAL.GRANTS_HINT")}
          </p>
          <CardListShell
            addLabel={t("STEP_TRIAL.ADD_GRANT")}
            onAdd={() => trialGrants.append({ meterKey: "", includedQuantity: 0 })}
          >
            {trialGrants.fields.map((field, index) => (
              <CardListItem key={field.id} onRemove={() => trialGrants.remove(index)}>
                <FormField
                  control={control}
                  name={`trialGrants.${index}.meterKey`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <FormLabel className="text-xs">{t("STEP_TRIAL.METER")}</FormLabel>
                      <Select value={inputField.value} onValueChange={inputField.onChange}>
                        <FormControl>
                          <SelectTrigger>
                            <SelectValue placeholder={t("STEP_TRIAL.CHOOSE_METER")} />
                          </SelectTrigger>
                        </FormControl>
                        <SelectContent>
                          {(meters ?? [])
                            .filter((meter) => meter.meterKey)
                            .map((meter) => (
                              <SelectItem key={meter.meterKey} value={meter.meterKey}>
                                {meter.displayName || meter.meterKey}
                              </SelectItem>
                            ))}
                        </SelectContent>
                      </Select>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={control}
                  name={`trialGrants.${index}.includedQuantity`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <FormLabel className="text-xs">{t("STEP_TRIAL.INCLUDED")}</FormLabel>
                      <FormControl>
                        {/* Stepped to the granularity of the meter this grant replaces, since a
                            grant it cannot hold is refused. */}
                        <Input
                          {...inputField}
                          type="number"
                          min={0}
                          step={stepFor(scaleOfNamedMeter(index))}
                        />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              </CardListItem>
            ))}
          </CardListShell>
        </div>
      )}
    </div>
  );
};
