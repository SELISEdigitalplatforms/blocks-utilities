import { useFieldArray, useFormContext, useWatch } from "react-hook-form";
import { ChevronDown } from "lucide-react";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui-kits/collapsible/collapsible";
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
import {
  BILLING_INTERVAL_OPTIONS,
  METER_AGGREGATION_OPTIONS,
  METER_RESET_POLICY_OPTIONS,
} from "../../constants/subscription.constants";
import type { PlanPrice } from "../../models/subscription-plan.model";
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import { METER_QUANTITY_MAX_SCALE, stepFor } from "../../utilities/meter-quantity";
import { CardListItem, CardListShell } from "./card-list-shell";
import { MeterRateTableFields } from "./meter-rate-table-fields";
import { PlanPriceFields } from "./plan-price-fields";
import { QuantityDiscountTiers } from "./quantity-discount-tiers";
import { ThresholdChipInput } from "./threshold-chip-input";

export const StepPricingModel = ({
  isEditing = false,
  existingPrices = [],
  onRetirePrice,
  onUpdatePriceTax,
  onUpdatePriceDiscount,
  retiringPriceId = null,
}: {
  isEditing?: boolean;
  existingPrices?: PlanPrice[];
  onRetirePrice?: (priceId: string) => void;
  onUpdatePriceTax?: (priceId: string, taxPercent?: number, taxMode?: "Exclusive" | "Inclusive") => Promise<void>;
  onUpdatePriceDiscount?: (priceId: string, discountPercent?: number, combination?: "BestDiscount" | "Additive") => Promise<void>;
  retiringPriceId?: string | null;
}) => {
  const { control, setValue } = useFormContext<CreateSubscriptionPlanFormValues>();

  const requirePaymentMethodUpfront = useWatch({
    control,
    name: "requirePaymentMethodUpfront",
  });
  const quantityItems = useFieldArray({ control, name: "quantityItems" });
  const meters = useFieldArray({ control, name: "meters" });
  // Watched, not read from useFieldArray's snapshot, which only refreshes when the list
  // itself changes — see step-usage-limits for the same trap.
  const meterValues = useWatch({ control, name: "meters" });
  const quantityItemValues = useWatch({ control, name: "quantityItems" });
  const hasBands = (quantityItemValues ?? []).some(
    (item) => (item?.quantityDiscountTiers?.length ?? 0) > 0,
  );

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-lg font-semibold">Pricing model</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Add any quantity and usage dimensions this plan needs. A flat-fee plan needs neither.
        </p>
      </div>

      <OptionalSection
        title="Quantity items"
        description="Use these when price scales with seats, users, or another selected quantity."
        defaultOpen={quantityItems.fields.length > 0}
      >
        <CardListShell
          addLabel="Add quantity item"
          onAdd={() =>
            quantityItems.append({
              itemKey: "",
              unitLabel: "",
              minQuantity: 1,
              defaultQuantity: 1,
              // No bands until asked for: a plan that sells at one price per unit is the common
              // case, and an empty list is what tells the API to store none.
              quantityDiscountTiers: [],
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
              <QuantityDiscountTiers itemIndex={index} />
            </CardListItem>
          ))}
        </CardListShell>

        {/* A plan-level decision, shown once the bands it governs exist. Hidden while no item has
            bands, because with none there is nothing for a promotion to combine with — but the
            value is still submitted, since a server that receives no policy resets the plan's to
            BestDiscount. */}
        {hasBands ? (
          <FormField
            control={control}
            name="quantityDiscountCombinationPolicy"
            render={({ field }) => (
              <FormItem className="mt-4">
                <FormLabel className="text-xs">
                  When a subscriber also has a discount code
                </FormLabel>
                <Select
                  value={String(field.value ?? 0)}
                  onValueChange={(value) => field.onChange(Number(value))}
                >
                  <FormControl>
                    <SelectTrigger>
                      <SelectValue />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    <SelectItem value="0">Apply whichever is larger</SelectItem>
                    <SelectItem value="1">Ignore the code, band only</SelectItem>
                    <SelectItem value="2">Apply both, compounding</SelectItem>
                  </SelectContent>
                </Select>
                <p className="text-xs text-muted-foreground">
                  Compounding gives away more than either discount alone, so choose it
                  deliberately. A code that loses to a band is not spent.
                </p>
                <FormMessage />
              </FormItem>
            )}
          />
        ) : null}
      </OptionalSection>

      <OptionalSection
        title="Meters"
        description="Use meters to track an allowance and optionally bill usage beyond it."
        defaultOpen={meters.fields.length > 0}
      >
        <CardListShell
          addLabel="Add meter"
          onAdd={() =>
            meters.append({
              meterKey: "",
              displayName: "",
              unitLabel: "",
              aggregation: 0,
              resetPolicy: 0,
              quantityScale: 0,
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
                name={`meters.${index}.quantityScale`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">Decimal places</FormLabel>
                    <FormControl>
                      <Input {...inputField} type="number" min={0} max={METER_QUANTITY_MAX_SCALE} />
                    </FormControl>
                    <p className="text-xs text-muted-foreground">
                      Leave at 0 for whole numbers only. Raise it when the unit is measured rather
                      than counted — gigabytes, hours. Set 1 to allow 512.5, or 3 to allow
                      512.505; more decimals than this are rejected.
                    </p>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={control}
                name={`meters.${index}.includedQuantity`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">
                      {meterValues?.[index]?.resetPolicy === 1
                        ? "Included for subscription lifetime"
                        : "Included per period"}
                    </FormLabel>
                    <FormControl>
                      {/* Stepped to the meter's own granularity. Without it the browser's own
                          number validation refuses a fraction before the form's ever sees it. */}
                      <Input
                        {...inputField}
                        type="number"
                        min={0}
                        step={stepFor(meterValues?.[index]?.quantityScale ?? 0)}
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={control}
                name={`meters.${index}.resetPolicy`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">Allowance resets</FormLabel>
                    <Select
                      value={String(inputField.value)}
                      onValueChange={(value) => {
                        const policy = Number(value);
                        inputField.onChange(policy);
                        if (policy === 1) {
                          setValue(`meters.${index}.overageAllowed`, false);
                          setValue(`meters.${index}.rateTables`, []);
                        }
                        // The cap belongs to carry-forward alone. Left behind on a switch it
                        // would submit a field the server rejects for that policy.
                        if (policy !== 2) {
                          setValue(`meters.${index}.carryForwardCap`, undefined);
                        }
                      }}
                    >
                      <FormControl>
                        <SelectTrigger>
                          <SelectValue />
                        </SelectTrigger>
                      </FormControl>
                      <SelectContent>
                        {METER_RESET_POLICY_OPTIONS.map((option) => (
                          <SelectItem key={option.value} value={String(option.value)}>
                            {option.label}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <p className="text-xs text-muted-foreground">
                      Choose Never for capacity that must remain consumed after renewal, or Carry
                      forward to let an unused allowance roll into the next period.
                    </p>
                    <FormMessage />
                  </FormItem>
                )}
              />
              {meterValues?.[index]?.resetPolicy === 2 && (
                <FormField
                  control={control}
                  name={`meters.${index}.carryForwardCap`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <FormLabel className="text-xs">Most one period may carry in</FormLabel>
                      <FormControl>
                        <Input
                          {...inputField}
                          value={inputField.value ?? ""}
                          type="number"
                          min={stepFor(meterValues?.[index]?.quantityScale ?? 0)}
                          step={stepFor(meterValues?.[index]?.quantityScale ?? 0)}
                        />
                      </FormControl>
                      <p className="text-xs text-muted-foreground">
                        Caps what rolls in, not the total — the included amount is always
                        available on top. Required, because without a ceiling a dormant
                        subscription banks allowance indefinitely.
                      </p>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              )}
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
              {meterValues?.[index]?.resetPolicy !== 1 ? (
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
              ) : (
                <p className="text-xs text-muted-foreground">
                  Lifetime capacity stops at its included amount and is not billed as monthly
                  overage.
                </p>
              )}
              <FormField
                control={control}
                name={`meters.${index}.thresholdPercents`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">Notify at</FormLabel>
                    <ThresholdChipInput value={inputField.value} onChange={inputField.onChange} />
                  </FormItem>
                )}
              />

              {/* Only meaningful where overage is permitted: a blocked meter never produces
                    billable units, so there is nothing to price. */}
              {meterValues?.[index]?.overageAllowed ? (
                <FormItem>
                  <FormLabel className="text-xs">Overage pricing</FormLabel>
                  {meterValues[index]?.rateTables?.length ? null : (
                    <p className="text-xs text-muted-foreground">
                      No price set, so usage past the allowance is billed nothing.
                    </p>
                  )}
                  <MeterRateTableFields meterIndex={index} />
                </FormItem>
              ) : null}
            </CardListItem>
          ))}
        </CardListShell>
        {(meterValues ?? []).some((meter) => meter.resetPolicy !== 1) && (
          <div className="grid gap-2 rounded-lg border p-3 sm:grid-cols-2">
            <FormField
              control={control}
              name="usageInterval"
              render={({ field }) => (
                <FormItem>
                  <FormLabel className="text-xs">Allowance resets every</FormLabel>
                  <Select
                    value={String(field.value)}
                    onValueChange={(value) => field.onChange(Number(value))}
                  >
                    <FormControl>
                      <SelectTrigger>
                        <SelectValue />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      {BILLING_INTERVAL_OPTIONS.map((option) => (
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
              name="usageIntervalCount"
              render={({ field }) => (
                <FormItem>
                  <FormLabel className="text-xs">How many</FormLabel>
                  <FormControl>
                    <Input {...field} type="number" min={1} max={100} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          </div>
        )}
      </OptionalSection>

      {/* Last in the step because a price may multiply a quantity item, which is defined above
          it. Not gated on the pricing shape: every plan needs a price, whatever its shape. */}
      <PlanPriceFields
        isEditing={isEditing}
        existingPrices={existingPrices}
        onRetirePrice={onRetirePrice}
        onUpdatePriceTax={onUpdatePriceTax}
        onUpdatePriceDiscount={onUpdatePriceDiscount}
        retiringPriceId={retiringPriceId}
      />

      {/*
        Governs activation, not any one price shape — including a plan with no trial and nothing
        due today. It sits after the prices it applies to rather than beside the trial's own
        card question, since a card requirement at signup is a billing decision, not a trial one.
      */}
      <div className="space-y-3 rounded-md border p-4">
        <h3 className="text-sm font-semibold">Payment method</h3>
        <FormField
          control={control}
          name="requirePaymentMethodUpfront"
          render={({ field }) => (
            <FormItem>
              <div className="flex items-center gap-2">
                <FormControl>
                  <Checkbox checked={field.value} onCheckedChange={field.onChange} />
                </FormControl>
                <FormLabel className="!m-0">
                  Require a payment method before activation, even when nothing is due today
                </FormLabel>
              </div>
              <p className="text-xs text-muted-foreground">
                {requirePaymentMethodUpfront
                  ? "The subscriber is sent to a card form that charges nothing. Nothing is granted until the card is stored, so the next period has something to bill."
                  : "A plan that costs nothing today starts straight away. Nothing has a card on file until the first charge, which is a problem only if there will be a later one."}
              </p>
            </FormItem>
          )}
        />
      </div>
    </div>
  );
};

const OptionalSection = ({
  title,
  description,
  defaultOpen,
  children,
}: {
  title: string;
  description: string;
  defaultOpen: boolean;
  children: React.ReactNode;
}) => (
  <Collapsible defaultOpen={defaultOpen} className="rounded-xl border p-4">
    <CollapsibleTrigger className="flex w-full items-center justify-between gap-3 text-left">
      <span>
        <span className="block text-sm font-semibold">{title}</span>
        <span className="block text-xs text-muted-foreground">{description}</span>
      </span>
      <ChevronDown className="h-4 w-4 shrink-0" />
    </CollapsibleTrigger>
    <CollapsibleContent className="space-y-3 pt-4">{children}</CollapsibleContent>
  </Collapsible>
);
