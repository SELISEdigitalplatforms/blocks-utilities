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
import { USAGE_WINDOW_NAMES, type PlanPrice } from "../../models/subscription-plan.model";
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import { METER_QUANTITY_MAX_SCALE, stepFor } from "../../utilities/meter-quantity";
import { CardListItem, CardListShell } from "./card-list-shell";
import { StepHeading } from "./step-heading";
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
  const isUserWise = useWatch({ control, name: "subscriberScope" }) === "User";
  // One item needs no mark — it is the one that counts people by elimination.
  const needsCountingMark = isUserWise && quantityItems.fields.length > 1;

  return (
    <div className="space-y-6">
      <StepHeading
        eyebrow="Pricing model"
        title="Pricing model"
        description="Add any quantity and usage dimensions this plan needs. A flat-fee plan needs neither."
      />

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
              countsMembers: false,
            })
          }
        >
          {isUserWise ? <PlacesExplanation /> : null}
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
              {needsCountingMark ? (
                <FormField
                  control={control}
                  name={`quantityItems.${index}.countsMembers`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <div className="flex items-center gap-2">
                        <FormControl>
                          <Checkbox
                            checked={inputField.value}
                            onCheckedChange={(checked) => inputField.onChange(checked === true)}
                          />
                        </FormControl>
                        <FormLabel className="!m-0 text-xs">This quantity counts people</FormLabel>
                      </div>
                    </FormItem>
                  )}
                />
              ) : null}
              <QuantityDiscountTiers itemIndex={index} />
            </CardListItem>
          ))}
        </CardListShell>
        {/* The "exactly one" rule is about the list, so its message sits under the list. */}
        <FormField control={control} name="quantityItems" render={() => <FormMessage />} />

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
              subLimitBehaviour: 0,
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
                        : `Included per period${
                            meterValues?.[index]?.unitLabel
                              ? ` (${meterValues[index].unitLabel})`
                              : ""
                          }`}
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
                    <FormLabel className="text-xs">Unused allowance</FormLabel>
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
                      Choose &ldquo;Does not apply&rdquo; for capacity that must remain consumed
                      after renewal, or let it roll into the next period instead.
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
                      <FormLabel className="text-xs">
                        Most {meterValues?.[index]?.unitLabel || "units"} one period may carry in
                      </FormLabel>
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
                        subscription banks allowance indefinitely. The most a subscriber can ever
                        hold in one period is the included amount plus this cap.
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
                    <FormLabel className="text-xs">How recordings are combined</FormLabel>
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
              <MeterPaceFields
                meterIndex={index}
                unitLabel={meterValues?.[index]?.unitLabel}
                quantityScale={meterValues?.[index]?.quantityScale ?? 0}
              />
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
                  <FormLabel className="text-xs">Allowance period</FormLabel>
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
                  <p className="text-xs text-muted-foreground">
                    How long one allowance lasts. Independent of how often you bill — a yearly
                    price can still grant a monthly allowance, and usually should.
                  </p>
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

/**
 * Which number a place count comes from depends on how the plan is priced, and that is the part
 * authors get wrong: the same quantity item means "how many were bought" on one price and "how many
 * may be bought" on another.
 */
const PlacesExplanation = () => (
  <p className="rounded-md bg-muted/60 p-3 text-xs leading-relaxed text-muted-foreground">
    On a plan for each person, the quantity that counts people decides how many places there are.{" "}
    <strong>Priced per unit</strong>, the quantity bought is how many places — 5 bought, 5 people.{" "}
    <strong>Priced flat</strong>, the maximum is — so a flat price needs a maximum set here.
  </p>
);

/**
 * A second cap measured in a short window, on top of the period's allowance. Collapsed unless the
 * meter already has one, so every meter that never opted in looks exactly as it did.
 */
const MeterPaceFields = ({
  meterIndex,
  unitLabel,
  quantityScale,
}: {
  meterIndex: number;
  unitLabel?: string;
  quantityScale: number;
}) => {
  const { control, setValue } = useFormContext<CreateSubscriptionPlanFormValues>();
  const paceWindow = useWatch({ control, name: `meters.${meterIndex}.subLimitWindow` });
  const hasPace = paceWindow !== undefined;

  return (
    <Collapsible defaultOpen={hasPace}>
      <CollapsibleTrigger className="flex items-center gap-1 text-xs font-medium text-muted-foreground hover:text-foreground">
        <ChevronDown className="h-3.5 w-3.5" />
        Pace (optional)
        {hasPace ? ` — per ${USAGE_WINDOW_NAMES[paceWindow].toLowerCase()}` : ""}
      </CollapsibleTrigger>
      <CollapsibleContent className="space-y-2 pt-2">
        <p className="text-xs text-muted-foreground">
          Caps how fast the allowance can be spent, not how much. Windows follow the clock — the
          hour, the day, or the ISO week starting Monday.
        </p>
        <div className="grid grid-cols-2 gap-2">
          <FormField
            control={control}
            name={`meters.${meterIndex}.subLimitWindow`}
            render={({ field }) => (
              <FormItem>
                <FormLabel className="text-xs">Window</FormLabel>
                <Select
                  value={field.value === undefined ? "none" : String(field.value)}
                  onValueChange={(value) => {
                    field.onChange(value === "none" ? undefined : Number(value));
                    // Taking the window away takes the cap with it: half a pace is refused.
                    if (value === "none") {
                      setValue(`meters.${meterIndex}.subLimitQuantity`, undefined);
                    }
                  }}
                >
                  <FormControl>
                    <SelectTrigger>
                      <SelectValue />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    <SelectItem value="none">No pace limit</SelectItem>
                    {USAGE_WINDOW_NAMES.map((name, value) => (
                      <SelectItem key={name} value={String(value)}>
                        Per {name.toLowerCase()}
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
            name={`meters.${meterIndex}.subLimitQuantity`}
            render={({ field }) => (
              <FormItem>
                <FormLabel className="text-xs">Most {unitLabel || "units"} per window</FormLabel>
                <FormControl>
                  <Input
                    {...field}
                    value={field.value ?? ""}
                    type="number"
                    min={stepFor(quantityScale)}
                    step={stepFor(quantityScale)}
                    disabled={!hasPace}
                  />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
        </div>
        {hasPace ? (
          <FormField
            control={control}
            name={`meters.${meterIndex}.subLimitBehaviour`}
            render={({ field }) => (
              <FormItem>
                <FormLabel className="text-xs">When the pace is exceeded</FormLabel>
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
                    <SelectItem value="0">Refuse the usage</SelectItem>
                    <SelectItem value="1">Allow it, and report it as over pace</SelectItem>
                  </SelectContent>
                </Select>
                <p className="text-xs text-muted-foreground">
                  Reporting slows nobody down by itself — the caller is told it is over pace and
                  decides what to do, e.g. fall back to a cheaper model.
                </p>
              </FormItem>
            )}
          />
        ) : null}
      </CollapsibleContent>
    </Collapsible>
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
  <Collapsible
    defaultOpen={defaultOpen}
    className="group/section rounded-xl border border-border/70 bg-card/60 p-4 transition-colors duration-200 hover:border-blocks-primary-200"
  >
    <CollapsibleTrigger className="flex w-full items-center justify-between gap-3 rounded-lg text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2">
      <span>
        <span className="block text-sm font-semibold text-high-emphasis">{title}</span>
        <span className="block text-xs leading-relaxed text-muted-foreground">{description}</span>
      </span>
      {/*
        Radix sets data-state on the trigger, so the chevron can point at the state it will move
        to without this component tracking `open` itself.
      */}
      <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full border border-border/70 bg-card text-muted-foreground transition-all duration-200 group-hover/section:border-blocks-primary-300 group-hover/section:text-blocks-primary-600">
        <ChevronDown className="h-4 w-4 transition-transform duration-300 group-data-[state=open]/section:rotate-180" />
      </span>
    </CollapsibleTrigger>
    {/*
      Fade/slide rather than the accordion height keyframes: those read
      `--radix-accordion-content-height`, which only Accordion publishes - Collapsible sets
      `--radix-collapsible-content-height`, so reusing them here would animate to no height at all.
    */}
    <CollapsibleContent className="space-y-3 pt-4 duration-200 data-[state=open]:animate-in data-[state=open]:fade-in data-[state=open]:slide-in-from-top-1">
      {children}
    </CollapsibleContent>
  </Collapsible>
);
