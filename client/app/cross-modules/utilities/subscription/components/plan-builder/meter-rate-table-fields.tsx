import { Plus, X } from "lucide-react";
import { useFieldArray, useFormContext, useWatch } from "react-hook-form";
import { Button } from "@/components/ui-kits/button/button";
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
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import {
  exampleMinorAmount,
  minorUnitStep,
} from "../../utilities/subscription-format";

/**
 * What usage past the allowance costs.
 *
 * Without a table for the subscription's currency the rater prices overage at zero — it is
 * recorded, permitted, and billed nothing — so a meter that allows overage and has no table
 * here is giving that usage away.
 */
export const MeterRateTableFields = ({ meterIndex }: { meterIndex: number }) => {
  const { control } = useFormContext<CreateSubscriptionPlanFormValues>();
  const tables = useFieldArray({
    control,
    name: `meters.${meterIndex}.rateTables`,
  });

  return (
    <div className="space-y-3 rounded-md border border-dashed p-3">
      {tables.fields.map((table, tableIndex) => (
        <RateTable
          key={table.id}
          meterIndex={meterIndex}
          tableIndex={tableIndex}
          onRemove={() => tables.remove(tableIndex)}
        />
      ))}

      <Button
        type="button"
        variant="outline"
        size="sm"
        className="w-full"
        onClick={() =>
          tables.append({
            currencyCode: SUBSCRIPTION_CURRENCY_OPTIONS[0].code,
            // One unbounded band is the smallest table that prices anything: every unit past
            // the allowance costs the same.
            tiers: [{ unitAmount: 0 }],
          })
        }
      >
        <Plus className="mr-2 h-4 w-4" />
        {tables.fields.length === 0 ? "Price the overage" : "Add another currency"}
      </Button>
    </div>
  );
};

/**
 * The message for a rule that belongs to the tier list as a whole rather than to any one input.
 * Read off the error tree because there is no field to hang a FormMessage on, and tolerant of
 * both shapes a field-array error arrives in.
 */
const useTiersError = (meterIndex: number, tableIndex: number): string | undefined => {
  const {
    formState: { errors },
  } = useFormContext<CreateSubscriptionPlanFormValues>();

  const tiers = errors.meters?.[meterIndex]?.rateTables?.[tableIndex]?.tiers as
    | { message?: string; root?: { message?: string } }
    | undefined;

  return tiers?.message ?? tiers?.root?.message;
};

const RateTable = ({
  meterIndex,
  tableIndex,
  onRemove,
}: {
  meterIndex: number;
  tableIndex: number;
  onRemove: () => void;
}) => {
  const { control, trigger } = useFormContext<CreateSubscriptionPlanFormValues>();
  const tiers = useFieldArray({
    control,
    name: `meters.${meterIndex}.rateTables.${tableIndex}.tiers`,
  });
  const currencyCode =
    useWatch({
      control,
      name: `meters.${meterIndex}.rateTables.${tableIndex}.currencyCode`,
    }) ?? SUBSCRIPTION_CURRENCY_OPTIONS[0].code;
  const tierValues = useWatch({
    control,
    name: `meters.${meterIndex}.rateTables.${tableIndex}.tiers`,
  });

  const tiersError = useTiersError(meterIndex, tableIndex);

  return (
    <div className="space-y-3">
      <div className="flex items-end gap-2">
        <FormField
          control={control}
          name={`meters.${meterIndex}.rateTables.${tableIndex}.currencyCode`}
          render={({ field }) => (
            <FormItem className="flex-1">
              <FormLabel className="text-xs">Currency</FormLabel>
              <Select
                value={field.value}
                onValueChange={(value) => {
                  field.onChange(value);
                  // The amounts above were valid for the currency that was chosen when they were
                  // typed. Nothing is converted — 0.05 francs is not 0.05 yen, and guessing which
                  // the author meant would be inventing a price — but the table is re-checked so
                  // an amount the new currency cannot express is named rather than rounded.
                  void trigger(`meters.${meterIndex}.rateTables.${tableIndex}`);
                }}
              >
                <FormControl>
                  <SelectTrigger>
                    <SelectValue />
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
        <Button
          type="button"
          variant="ghost"
          size="icon"
          onClick={onRemove}
          aria-label="Remove currency"
          className="text-muted-foreground hover:text-destructive"
        >
          <X className="h-4 w-4" />
        </Button>
      </div>

      {tiers.fields.map((tier, tierIndex) => {
        const isLast = tierIndex === tiers.fields.length - 1;

        return (
          <div key={tier.id} className="flex items-end gap-2">
            <FormField
              control={control}
              name={`meters.${meterIndex}.rateTables.${tableIndex}.tiers.${tierIndex}.upToQuantity`}
              render={({ field }) => (
                <FormItem className="flex-1">
                  <FormLabel className="text-xs">
                    {isLast ? "Everything above" : "Up to"}
                  </FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      value={field.value ?? ""}
                      type="number"
                      min={1}
                      // The final band has no upper bound — one that did would leave usage
                      // past it unpriced.
                      disabled={isLast}
                      placeholder={isLast ? "no limit" : "1000"}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={control}
              name={`meters.${meterIndex}.rateTables.${tableIndex}.tiers.${tierIndex}.unitAmount`}
              render={({ field }) => (
                <FormItem className="flex-1">
                  {/* The currency is named on the label rather than left to the select above,
                      because the amount is meaningless without it and the two are read
                      separately once a meter has tables in several currencies. */}
                  <FormLabel className="text-xs">Per unit ({currencyCode})</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      type="number"
                      min={0}
                      step={minorUnitStep(currencyCode)}
                      placeholder={exampleMinorAmount(currencyCode)}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <Button
              type="button"
              variant="ghost"
              size="icon"
              onClick={() => tiers.remove(tierIndex)}
              disabled={tiers.fields.length === 1}
              aria-label="Remove tier"
              className="text-muted-foreground hover:text-destructive"
            >
              <X className="h-4 w-4" />
            </Button>
          </div>
        );
      })}

      {/* The ordering rule is checked on the whole table, not on any one input, so it has no
          field of its own to render under — read straight off the error tree instead. Without
          this an invalid table looked fine right up until the server rejected it. A field-array
          error can land on the array or under its root depending on how it was raised, so both
          are checked. */}
      {tiersError ? (
        <p className="text-sm font-medium text-destructive">{tiersError}</p>
      ) : null}

      <Button
        type="button"
        variant="ghost"
        size="sm"
        onClick={() => {
          const lastIndex = tiers.fields.length - 1;
          const last = tierValues?.[lastIndex];

          // The band being closed is the open-ended one, so it never has a bound of its own to
          // reuse. Deriving the suggestion from the band before it is what keeps the table
          // ascending — defaulting to a constant put two bands on the same bound as soon as
          // this was clicked twice, which the server rejects.
          const previousBound = tierValues?.[lastIndex - 1]?.upToQuantity;

          tiers.update(lastIndex, {
            upToQuantity: previousBound ? previousBound * 2 : 1_000,
            unitAmount: last?.unitAmount ?? 0,
          });
          tiers.append({ unitAmount: 0 });
        }}
      >
        <Plus className="mr-2 h-4 w-4" />
        Add another band
      </Button>

      <p className="text-xs text-muted-foreground">
        Amounts are in {currencyCode} — {exampleMinorAmount(currencyCode)} means{" "}
        {exampleMinorAmount(currencyCode)} {currencyCode} per extra unit.
      </p>
    </div>
  );
};
