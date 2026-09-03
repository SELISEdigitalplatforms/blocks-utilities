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

const TRIAL_DURATION_KIND_OPTIONS: {
  value: "Days" | "EndOfCalendarMonth" | "AnniversaryMonths";
  label: string;
  description: string;
}[] = [
  {
    value: "Days",
    label: "Fixed number of days",
    description: "14 days from signup.",
  },
  {
    value: "EndOfCalendarMonth",
    label: "Until the end of the calendar month",
    description:
      "Until the end of the calendar month; late signups may receive a short trial.",
  },
  {
    value: "AnniversaryMonths",
    label: "Fixed number of months",
    description: "Same local date and time after N months.",
  },
];

export const StepTrial = () => {
  const { control, setValue } = useFormContext<CreateSubscriptionPlanFormValues>();
  const trialGrants = useFieldArray({ control, name: "trialGrants" });
  const trialDurationKind = useWatch({ control, name: "trialDurationKind" });
  const trialRequiresPaymentMethod = useWatch({
    control,
    name: "trialRequiresPaymentMethod",
  });
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
        eyebrow="Trial"
        title="Trial"
        description={'Optional. Choose “No trial” for a plan that charges from day one.'}
      />

      <div className="grid gap-5 sm:grid-cols-2">
        <FormField
          control={control}
          name="trialDurationKind"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Trial duration</FormLabel>
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
                  <SelectItem value={NO_TRIAL}>No trial</SelectItem>
                  {TRIAL_DURATION_KIND_OPTIONS.map((option) => (
                    <SelectItem key={option.value} value={option.value}>
                      {option.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                {trialDurationKind
                  ? TRIAL_DURATION_KIND_OPTIONS.find((option) => option.value === trialDurationKind)
                      ?.description
                  : "A plan that charges from day one."}
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
                  {trialDurationKind === "AnniversaryMonths" ? "Months" : "Days"}
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
                <FormLabel className="!m-0">Require a card to start the trial</FormLabel>
              </div>
              {/*
                Both branches describe a trial that is free until it ends. Requiring a card and
                charging for the first period used to be the same act, because the money path could
                not hold a card without taking money with it, and this copy said so. Card setup
                separated them: the card is stored by a setup session that charges nothing.
              */}
              <p className="text-xs text-muted-foreground">
                {trialRequiresPaymentMethod
                  ? "A card is saved now without a charge. The first paid period is charged when the trial ends."
                  : "The trial starts without a card. A payment method is required before paid access can continue."}
              </p>
            </FormItem>
          )}
        />
      </div>

      {Boolean(trialDurationKind) && (
        <div className="space-y-3">
          <h3 className="text-sm font-semibold">Trial grants</h3>
          <p className="text-xs text-muted-foreground">
            Give a trial its own allowance for a meter instead of the plan&apos;s normal one —
            useful when the regular allowance would be an open invitation to sign up, consume and
            leave.
          </p>
          <CardListShell
            addLabel="Add trial grant"
            onAdd={() => trialGrants.append({ meterKey: "", includedQuantity: 0 })}
          >
            {trialGrants.fields.map((field, index) => (
              <CardListItem key={field.id} onRemove={() => trialGrants.remove(index)}>
                <FormField
                  control={control}
                  name={`trialGrants.${index}.meterKey`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <FormLabel className="text-xs">Meter</FormLabel>
                      <Select value={inputField.value} onValueChange={inputField.onChange}>
                        <FormControl>
                          <SelectTrigger>
                            <SelectValue placeholder="Choose a meter" />
                          </SelectTrigger>
                        </FormControl>
                        <SelectContent>
                          {(meters ?? [])
                            .filter((meter) => meter.meterKey && meter.resetPolicy !== 1)
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
                      <FormLabel className="text-xs">Included during trial</FormLabel>
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
