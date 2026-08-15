import { Gauge, Layers, Plus } from "lucide-react";
import { useFieldArray, useFormContext, useWatch } from "react-hook-form";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import {
  FormControl,
  FormDescription,
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
import { METER_AGGREGATION_OPTIONS } from "../../constants/subscription.constants";
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import { CardListItem, CardListShell } from "./card-list-shell";
import { ThresholdChipInput } from "./threshold-chip-input";

const PRICING_SHAPES = [
  {
    value: "seats" as const,
    icon: Layers,
    title: "Per seat or unit",
    description: "Charge per seat, user, or unit — e.g. $12 per user, per month.",
  },
  {
    value: "usage" as const,
    icon: Gauge,
    title: "Usage-based",
    description: "Charge for what's used — e.g. $0.01 per API call, with a free allowance.",
  },
  {
    value: "both" as const,
    icon: Plus,
    title: "Both",
    description: "A base seat charge plus metered usage on top.",
  },
];

export const StepPricingModel = () => {
  const { control, setValue } = useFormContext<CreateSubscriptionPlanFormValues>();
  const pricingShape = useWatch({ control, name: "pricingShape" });

  const quantityItems = useFieldArray({ control, name: "quantityItems" });
  const meters = useFieldArray({ control, name: "meters" });

  const showSeats = pricingShape === "seats" || pricingShape === "both";
  const showUsage = pricingShape === "usage" || pricingShape === "both";

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-lg font-semibold">Pricing model</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          How does this plan charge? Pick the shape that fits — you can change this later.
        </p>
      </div>

      <div className="grid gap-3 sm:grid-cols-3">
        {PRICING_SHAPES.map((shape) => (
          <button
            key={shape.value}
            type="button"
            onClick={() => setValue("pricingShape", shape.value, { shouldDirty: true })}
            className={`rounded-xl border p-4 text-left transition ${
              pricingShape === shape.value
                ? "border-blocks-primary-500 bg-blocks-primary-shades-50"
                : "hover:border-blocks-primary-300"
            }`}
          >
            <shape.icon className="h-5 w-5 text-blocks-primary-600" />
            <p className="mt-2 font-medium">{shape.title}</p>
            <p className="mt-1 text-xs text-muted-foreground">{shape.description}</p>
          </button>
        ))}
      </div>

      {showSeats && (
        <div className="space-y-3">
          <h3 className="text-sm font-semibold">Quantity items</h3>
          <CardListShell
            addLabel="Add quantity item"
            onAdd={() =>
              quantityItems.append({
                itemKey: "",
                unitLabel: "",
                minQuantity: 1,
                defaultQuantity: 1,
              })
            }
          >
            {quantityItems.fields.map((field, index) => (
              <CardListItem key={field.id} onRemove={() => quantityItems.remove(index)}>
                <FormField
                  control={control}
                  name={`quantityItems.${index}.itemKey`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <FormLabel className="text-xs">Item key</FormLabel>
                      <FormControl>
                        <Input {...inputField} placeholder="seat" />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={control}
                  name={`quantityItems.${index}.unitLabel`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <FormLabel className="text-xs">Unit label</FormLabel>
                      <FormControl>
                        <Input {...inputField} placeholder="seat" />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <div className="grid grid-cols-2 gap-2">
                  <FormField
                    control={control}
                    name={`quantityItems.${index}.defaultQuantity`}
                    render={({ field: inputField }) => (
                      <FormItem>
                        <FormLabel className="text-xs">Default qty</FormLabel>
                        <FormControl>
                          <Input {...inputField} type="number" min={0} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    control={control}
                    name={`quantityItems.${index}.maxQuantity`}
                    render={({ field: inputField }) => (
                      <FormItem>
                        <FormLabel className="text-xs">Max (optional)</FormLabel>
                        <FormControl>
                          <Input {...inputField} type="number" min={0} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>
              </CardListItem>
            ))}
          </CardListShell>
        </div>
      )}

      {showUsage && (
        <div className="space-y-3">
          <h3 className="text-sm font-semibold">Meters</h3>
          <CardListShell
            addLabel="Add meter"
            onAdd={() =>
              meters.append({
                meterKey: "",
                displayName: "",
                unitLabel: "",
                aggregation: 0,
                includedQuantity: 0,
                overageAllowed: true,
                thresholdPercents: [],
                rateTables: [],
              })
            }
          >
            {meters.fields.map((field, index) => (
              <CardListItem key={field.id} onRemove={() => meters.remove(index)}>
                <FormField
                  control={control}
                  name={`meters.${index}.displayName`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <FormLabel className="text-xs">Display name</FormLabel>
                      <FormControl>
                        <Input {...inputField} placeholder="API calls" />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <div className="grid grid-cols-2 gap-2">
                  <FormField
                    control={control}
                    name={`meters.${index}.meterKey`}
                    render={({ field: inputField }) => (
                      <FormItem>
                        <FormLabel className="text-xs">Meter key</FormLabel>
                        <FormControl>
                          <Input {...inputField} placeholder="api-calls" />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    control={control}
                    name={`meters.${index}.unitLabel`}
                    render={({ field: inputField }) => (
                      <FormItem>
                        <FormLabel className="text-xs">Unit label</FormLabel>
                        <FormControl>
                          <Input {...inputField} placeholder="call" />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>
                <FormField
                  control={control}
                  name={`meters.${index}.includedQuantity`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <FormLabel className="text-xs">Included per period</FormLabel>
                      <FormControl>
                        <Input {...inputField} type="number" min={0} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={control}
                  name={`meters.${index}.aggregation`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <FormLabel className="text-xs">How usage is measured</FormLabel>
                      <Select
                        value={String(inputField.value)}
                        onValueChange={(value) => inputField.onChange(Number(value))}
                      >
                        <FormControl>
                          <SelectTrigger>
                            <SelectValue />
                          </SelectTrigger>
                        </FormControl>
                        <SelectContent>
                          {METER_AGGREGATION_OPTIONS.map((option) => (
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
                <FormField
                  control={control}
                  name={`meters.${index}.overageAllowed`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <div className="flex items-center gap-2">
                        <FormControl>
                          <Checkbox
                            checked={inputField.value}
                            onCheckedChange={inputField.onChange}
                          />
                        </FormControl>
                        <FormLabel className="!m-0 text-xs">
                          Allow going over the included amount (billed as overage)
                        </FormLabel>
                      </div>
                    </FormItem>
                  )}
                />
                <FormField
                  control={control}
                  name={`meters.${index}.thresholdPercents`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <FormLabel className="text-xs">Notify at</FormLabel>
                      <ThresholdChipInput
                        value={inputField.value}
                        onChange={inputField.onChange}
                      />
                    </FormItem>
                  )}
                />
              </CardListItem>
            ))}
          </CardListShell>
        </div>
      )}

      {!pricingShape && (
        <Card className="flex flex-col items-center gap-2 rounded-xl py-8 text-center text-sm text-muted-foreground">
          Choose a pricing shape above to continue.
        </Card>
      )}
    </div>
  );
};
