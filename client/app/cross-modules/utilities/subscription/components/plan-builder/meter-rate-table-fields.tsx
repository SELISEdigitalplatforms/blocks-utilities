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
            tiers: [{ unitAmountMinor: 0 }],
          })
        }
      >
        <Plus className="mr-2 h-4 w-4" />
        {tables.fields.length === 0 ? "Price the overage" : "Add another currency"}
      </Button>
    </div>
  );
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
  const { control } = useFormContext<CreateSubscriptionPlanFormValues>();
  const tiers = useFieldArray({
    control,
    name: `meters.${meterIndex}.rateTables.${tableIndex}.tiers`,
  });
  const tierValues = useWatch({
    control,
    name: `meters.${meterIndex}.rateTables.${tableIndex}.tiers`,
  });

  return (
    <div className="space-y-3">
      <div className="flex items-end gap-2">
        <FormField
          control={control}
          name={`meters.${meterIndex}.rateTables.${tableIndex}.currencyCode`}
          render={({ field }) => (
            <FormItem className="flex-1">
              <FormLabel className="text-xs">Currency</FormLabel>
              <Select value={field.value} onValueChange={field.onChange}>
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
              name={`meters.${meterIndex}.rateTables.${tableIndex}.tiers.${tierIndex}.unitAmountMinor`}
              render={({ field }) => (
                <FormItem className="flex-1">
                  <FormLabel className="text-xs">Per unit (minor)</FormLabel>
                  <FormControl>
                    <Input {...field} type="number" min={0} placeholder="5" />
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

      <Button
        type="button"
        variant="ghost"
        size="sm"
        onClick={() => {
          // The band that was unbounded gains a bound, and the new one becomes the open end,
          // so the table always ends somewhere that prices the rest.
          const last = tierValues?.[tiers.fields.length - 1];

          tiers.update(tiers.fields.length - 1, {
            upToQuantity: last?.upToQuantity ?? 1_000,
            unitAmountMinor: last?.unitAmountMinor ?? 0,
          });
          tiers.append({ unitAmountMinor: 0 });
        }}
      >
        <Plus className="mr-2 h-4 w-4" />
        Add a cheaper band above
      </Button>

      <p className="text-xs text-muted-foreground">
        Amounts are in minor units — 5 means 5 cents per extra unit.
      </p>
    </div>
  );
};
