import { useFieldArray, useFormContext, useWatch, type Control } from "react-hook-form";
import { Button } from "@/components/ui-kits/button/button";
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
import { SUBSCRIPTION_CURRENCY_OPTIONS } from "../../constants/subscription.constants";
import {
  CREDIT_EXPIRY_UNIT,
  FIXED_CREDIT_EXPIRY,
  METER_RESET_POLICY,
} from "../../models/subscription-plan.model";
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import { TAX_MODE_OPTIONS } from "../../utilities/subscription-tax";
import { CardListItem, CardListShell } from "./card-list-shell";
import { StepHeading } from "./step-heading";

/** When bought credits stop being usable, worded for the person selling them. */
export const CREDIT_EXPIRY_OPTIONS: { value: number; label: string; description: string }[] = [
  {
    value: METER_RESET_POLICY.Periodic,
    label: "With the usage window",
    description: "Credits not used by the end of the window they were bought in are lost.",
  },
  {
    value: METER_RESET_POLICY.CarryForward,
    label: "Carry forward",
    description: "Unused credits roll into each renewal, and are lost when the plan changes.",
  },
  {
    value: METER_RESET_POLICY.Never,
    label: "Never expire",
    description: "Credits last as long as the subscription and this meter do.",
  },
  {
    value: FIXED_CREDIT_EXPIRY,
    label: "After a fixed time",
    description: "Credits last a set time from the moment they are bought, whatever the billing calendar does.",
  },
];

/** The units a fixed lifetime can be counted in, in the order a person would list them. */
export const CREDIT_EXPIRY_UNIT_OPTIONS: { value: number; label: string }[] = [
  { value: CREDIT_EXPIRY_UNIT.Hour, label: "Hours" },
  { value: CREDIT_EXPIRY_UNIT.Day, label: "Days" },
  { value: CREDIT_EXPIRY_UNIT.Week, label: "Weeks" },
  { value: CREDIT_EXPIRY_UNIT.Month, label: "Months" },
  { value: CREDIT_EXPIRY_UNIT.Year, label: "Years" },
];

/**
 * Selling extra credits for a meter: what one costs, how many may be bought at once, and when
 * they expire. Bought credits are spent after the plan's own allowance and before any overage.
 */
export const StepCredits = () => {
  const { control } = useFormContext<CreateSubscriptionPlanFormValues>();
  const credits = useFieldArray({ control, name: "meterCredits" });
  const meters = useWatch({ control, name: "meters" }) ?? [];
  const values = useWatch({ control, name: "meterCredits" }) ?? [];

  const namedMeters = meters.filter((meter) => meter.meterKey);
  const unused = namedMeters.filter(
    (meter) => !values.some((credit) => credit?.meterKey === meter.meterKey),
  );

  return (
    <div className="space-y-6">
      <StepHeading
        eyebrow="Credits"
        title="Sell extra credits"
        description="Optional. Let subscribers buy more of a meter up front, at a price per credit. Bought credits are used after the plan's own allowance runs out, and before any overage."
      />

      {namedMeters.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          Add a meter in the pricing step first. Credits are sold for a meter the plan counts.
        </p>
      ) : (
        <CardListShell
          addLabel="Sell credits for a meter"
          columns={1}
          onAdd={() =>
            credits.append({
              // The first meter without terms. Empty once every meter has some, which the schema
              // then asks the author to choose.
              meterKey: unused[0]?.meterKey ?? "",
              enabled: true,
              prices: [{ currencyCode: "USD", unitAmount: 0.1 }],
              minQuantity: 1,
              maxQuantity: 10_000,
              expiryPolicy: METER_RESET_POLICY.Periodic,
              expiresAfterCount: undefined,
              expiresAfterUnit: CREDIT_EXPIRY_UNIT.Day,
              taxPercent: undefined,
              taxMode: "Exclusive",
            })
          }
        >
          {credits.fields.map((field, index) => (
            <CardListItem key={field.id} onRemove={() => credits.remove(index)}>
              <CreditFields control={control} index={index} meters={namedMeters} />
            </CardListItem>
          ))}
        </CardListShell>
      )}
    </div>
  );
};

const CreditFields = ({
  control,
  index,
  meters,
}: {
  control: Control<CreateSubscriptionPlanFormValues>;
  index: number;
  meters: { meterKey: string; displayName?: string }[];
}) => {
  const prices = useFieldArray({ control, name: `meterCredits.${index}.prices` });
  const enabled = useWatch({ control, name: `meterCredits.${index}.enabled` });
  const expiry = useWatch({ control, name: `meterCredits.${index}.expiryPolicy` });
  const taxPercent = useWatch({ control, name: `meterCredits.${index}.taxPercent` });

  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField
          control={control}
          name={`meterCredits.${index}.meterKey`}
          render={({ field }) => (
            <FormItem>
              <FormLabel className="text-xs">Meter</FormLabel>
              <Select value={field.value} onValueChange={field.onChange}>
                <FormControl>
                  <SelectTrigger aria-label="Meter">
                    <SelectValue placeholder="Choose a meter" />
                  </SelectTrigger>
                </FormControl>
                <SelectContent>
                  {meters.map((meter) => (
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
          name={`meterCredits.${index}.enabled`}
          render={({ field }) => (
            <FormItem className="flex flex-col justify-end">
              <div className="flex items-center gap-2">
                <FormControl>
                  <Checkbox checked={field.value} onCheckedChange={field.onChange} />
                </FormControl>
                <FormLabel className="!m-0">On sale</FormLabel>
              </div>
              <p className="text-xs text-muted-foreground">
                {enabled
                  ? "Subscribers can buy these credits."
                  : "Paused. The terms are kept, and nobody can buy until you switch this back on."}
              </p>
            </FormItem>
          )}
        />
      </div>

      <div className="space-y-2">
        <h4 className="text-xs font-semibold">Price per credit</h4>
        {prices.fields.map((priceField, priceIndex) => (
          <div key={priceField.id} className="grid grid-cols-[6rem_minmax(0,1fr)_auto] items-start gap-2">
            <FormField
              control={control}
              name={`meterCredits.${index}.prices.${priceIndex}.currencyCode`}
              render={({ field }) => (
                <FormItem>
                  <Select value={field.value} onValueChange={field.onChange}>
                    <FormControl>
                      <SelectTrigger aria-label="Currency">
                        <SelectValue placeholder="Currency" />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      {SUBSCRIPTION_CURRENCY_OPTIONS.map((currency) => (
                        <SelectItem key={currency.code} value={currency.code}>
                          {currency.code}
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
              name={`meterCredits.${index}.prices.${priceIndex}.unitAmount`}
              render={({ field }) => (
                <FormItem>
                  <FormControl>
                    <Input {...field} aria-label="Price per credit" type="number" min={0} step="any" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => prices.remove(priceIndex)}
              aria-label="Remove price"
            >
              Remove
            </Button>
          </div>
        ))}
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => prices.append({ currencyCode: "", unitAmount: 0 })}
        >
          Add a currency
        </Button>
        <FormField
          control={control}
          name={`meterCredits.${index}.prices`}
          render={() => (
            <FormItem>
              <FormMessage />
            </FormItem>
          )}
        />
        <p className="text-xs text-muted-foreground">
          A subscriber is offered credits only in their subscription&apos;s currency. At 0.10, 100
          credits cost 10.00.
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-3">
        <FormField
          control={control}
          name={`meterCredits.${index}.minQuantity`}
          render={({ field }) => (
            <FormItem>
              <FormLabel className="text-xs">Smallest purchase</FormLabel>
              <FormControl>
                <Input {...field} type="number" min={1} step={1} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name={`meterCredits.${index}.maxQuantity`}
          render={({ field }) => (
            <FormItem>
              <FormLabel className="text-xs">Largest purchase</FormLabel>
              <FormControl>
                <Input {...field} value={field.value ?? ""} type="number" min={1} step={1} />
              </FormControl>
              <p className="text-xs text-muted-foreground">Bounds what one purchase can charge.</p>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name={`meterCredits.${index}.expiryPolicy`}
          render={({ field }) => (
            <FormItem>
              <FormLabel className="text-xs">Credits expire</FormLabel>
              <Select
                value={String(field.value)}
                onValueChange={(value) => field.onChange(Number(value))}
              >
                <FormControl>
                  <SelectTrigger aria-label="Credits expire">
                    <SelectValue />
                  </SelectTrigger>
                </FormControl>
                <SelectContent>
                  {CREDIT_EXPIRY_OPTIONS.map((option) => (
                    <SelectItem key={option.value} value={String(option.value)}>
                      {option.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                {CREDIT_EXPIRY_OPTIONS.find((option) => option.value === expiry)?.description}
              </p>
              <FormMessage />
            </FormItem>
          )}
        />
      </div>

      {expiry === FIXED_CREDIT_EXPIRY && (
        <div className="grid grid-cols-[8rem_minmax(0,1fr)] gap-2 sm:max-w-sm">
          <FormField
            control={control}
            name={`meterCredits.${index}.expiresAfterCount`}
            render={({ field }) => (
              <FormItem>
                <FormLabel className="text-xs">Credits last</FormLabel>
                <FormControl>
                  <Input {...field} value={field.value ?? ""} aria-label="Credits last" type="number" min={1} step={1} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={control}
            name={`meterCredits.${index}.expiresAfterUnit`}
            render={({ field }) => (
              <FormItem>
                <FormLabel className="text-xs">&nbsp;</FormLabel>
                <Select value={String(field.value)} onValueChange={(value) => field.onChange(Number(value))}>
                  <FormControl>
                    <SelectTrigger aria-label="Lifetime unit">
                      <SelectValue />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    {CREDIT_EXPIRY_UNIT_OPTIONS.map((option) => (
                      <SelectItem key={option.value} value={String(option.value)}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <FormMessage />
              </FormItem>
            )}
          />
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        <FormField
          control={control}
          name={`meterCredits.${index}.taxPercent`}
          render={({ field }) => (
            <FormItem>
              <FormLabel className="text-xs">Tax rate (%)</FormLabel>
              <FormControl>
                <Input {...field} value={field.value ?? ""} type="number" min={0} max={100} step="any" />
              </FormControl>
              <p className="text-xs text-muted-foreground">Leave empty for untaxed credits.</p>
              <FormMessage />
            </FormItem>
          )}
        />
        {Boolean(taxPercent) && (
          <FormField
            control={control}
            name={`meterCredits.${index}.taxMode`}
            render={({ field }) => (
              <FormItem>
                <FormLabel className="text-xs">Tax is</FormLabel>
                <Select value={field.value} onValueChange={field.onChange}>
                  <FormControl>
                    <SelectTrigger aria-label="Tax is">
                      <SelectValue />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    {TAX_MODE_OPTIONS.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <FormMessage />
              </FormItem>
            )}
          />
        )}
      </div>
    </div>
  );
};
