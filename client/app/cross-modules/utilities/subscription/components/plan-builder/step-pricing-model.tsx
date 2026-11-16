import { useT } from "@seliseblocks/genesis-os";
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
import {
  BILLING_INTERVAL_OPTIONS,
  METER_AGGREGATION_OPTIONS,
  METER_RESET_POLICY_OPTIONS,
} from "../../constants/subscription.constants";
import type { PlanPrice } from "../../models/subscription-plan.model";
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import { METER_QUANTITY_MAX_SCALE, stepFor } from "../../utilities/meter-quantity";
import { RichText } from "../rich-text";
import { CardListItem, CardListShell } from "./card-list-shell";
import { CatalogueMeterPicker } from "./catalogue-meter-picker";
import { StepHeading } from "./step-heading";
import { MeterRateTableFields } from "./meter-rate-table-fields";
import { MeterPaceFields } from "./meter-pace-fields";
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
  const { t } = useT("utilities.subscription.plan-builder");
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
  const includedLabel = (meter: (typeof meterValues)[number] | undefined): string => {
    if (meter?.resetPolicy === 1) return t("STEP_PRICING_MODEL.INCLUDED_LIFETIME");
    if (meter?.unitLabel) return t("STEP_PRICING_MODEL.INCLUDED_PER_PERIOD_UNIT", { unit: meter.unitLabel });
    return t("STEP_PRICING_MODEL.INCLUDED_PER_PERIOD");
  };
  const quantityItemValues = useWatch({ control, name: "quantityItems" });
  const hasBands = (quantityItemValues ?? []).some(
    (item) => (item?.quantityDiscountTiers?.length ?? 0) > 0,
  );
  const priceValues = useWatch({ control, name: "prices" });
  const isUserWise = useWatch({ control, name: "subscriberScope" }) === "User";
  const placesMode = placesModeOf(quantityItemValues, priceValues);
  // One item needs no mark — it is the one that counts people by elimination.
  const needsCountingMark = isUserWise && quantityItems.fields.length > 1;

  return (
    <div className="space-y-6">
      <StepHeading
        eyebrow={t("STEP_PRICING_MODEL.TITLE")}
        title={t("STEP_PRICING_MODEL.TITLE")}
        description={t("STEP_PRICING_MODEL.DESCRIPTION")}
      />

      <OptionalSection
        title={t("STEP_PRICING_MODEL.QUANTITY_ITEMS")}
        description={t("STEP_PRICING_MODEL.QUANTITY_ITEMS_HINT")}
        defaultOpen={quantityItems.fields.length > 0}
      >
        <CardListShell
          addLabel={t("STEP_PRICING_MODEL.ADD_QUANTITY_ITEM")}
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
          {isUserWise ? <PlacesExplanation mode={placesMode} /> : null}
          {quantityItems.fields.map((field, index) => (
            <CardListItem key={field.id} onRemove={() => quantityItems.remove(index)}>
              <FormField
                control={control}
                name={`quantityItems.${index}.itemKey`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.ITEM_KEY")}</FormLabel>
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
                    <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.UNIT_LABEL")}</FormLabel>
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
                      <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.DEFAULT_QTY")}</FormLabel>
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
                      <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.MAX_OPTIONAL")}</FormLabel>
                      <FormControl>
                        <Input {...inputField} type="number" min={0} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              </div>
              <FormField
                control={control}
                name={`quantityItems.${index}.trialMaxQuantity`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.TRIAL_MAX_OPTIONAL")}</FormLabel>
                    <FormControl>
                      <Input {...inputField} value={inputField.value ?? ""} type="number" min={1} />
                    </FormControl>
                    <FormDescription className="text-xs">
                      {t("STEP_PRICING_MODEL.TRIAL_MAX_HINT")}
                    </FormDescription>
                    <FormMessage />
                  </FormItem>
                )}
              />

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
                        <FormLabel className="!m-0 text-xs">{t("STEP_PRICING_MODEL.COUNTS_PEOPLE")}</FormLabel>
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
                  {t("STEP_PRICING_MODEL.DISCOUNT_CODE_POLICY")}
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
                    <SelectItem value="0">{t("STEP_PRICING_MODEL.POLICY_LARGER")}</SelectItem>
                    <SelectItem value="1">{t("STEP_PRICING_MODEL.POLICY_BAND_ONLY")}</SelectItem>
                    <SelectItem value="2">{t("STEP_PRICING_MODEL.POLICY_COMPOUND")}</SelectItem>
                  </SelectContent>
                </Select>
                <p className="text-xs text-muted-foreground">
                  {t("STEP_PRICING_MODEL.POLICY_HINT")}
                </p>
                <FormMessage />
              </FormItem>
            )}
          />
        ) : null}
      </OptionalSection>

      <OptionalSection
        title={t("STEP_PRICING_MODEL.METERS")}
        description={t("STEP_PRICING_MODEL.METERS_HINT")}
        defaultOpen={meters.fields.length > 0}
      >
        <CardListShell
          columns={1}
          addLabel={t("STEP_PRICING_MODEL.ADD_METER")}
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
              subLimits: [],
            })
          }
        >
          {meters.fields.map((field, index) => (
            <CardListItem key={field.id} onRemove={() => meters.remove(index)}>
              <CatalogueMeterPicker
                onPick={(picked) => {
                  setValue(`meters.${index}.meterKey`, picked.meterKey, { shouldDirty: true });
                  setValue(`meters.${index}.displayName`, picked.displayName, { shouldDirty: true });
                  setValue(`meters.${index}.unitLabel`, picked.unitLabel, { shouldDirty: true });
                  setValue(`meters.${index}.aggregation`, picked.aggregation, { shouldDirty: true });
                  setValue(`meters.${index}.quantityScale`, picked.quantityScale, { shouldDirty: true });
                }}
              />
              <FormField
                control={control}
                name={`meters.${index}.displayName`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.DISPLAY_NAME")}</FormLabel>
                    <FormControl>
                      <Input {...inputField} placeholder={t("STEP_PRICING_MODEL.DISPLAY_NAME_PLACEHOLDER")} />
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
                      <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.METER_KEY")}</FormLabel>
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
                      <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.UNIT_LABEL")}</FormLabel>
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
                    <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.DECIMAL_PLACES")}</FormLabel>
                    <FormControl>
                      <Input {...inputField} type="number" min={0} max={METER_QUANTITY_MAX_SCALE} />
                    </FormControl>
                    <p className="text-xs text-muted-foreground">
                      {t("STEP_PRICING_MODEL.DECIMAL_PLACES_HINT")}
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
                      {includedLabel(meterValues?.[index])}
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
                    <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.UNUSED_ALLOWANCE")}</FormLabel>
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
                      {t("STEP_PRICING_MODEL.UNUSED_HINT")}
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
                        {t("STEP_PRICING_MODEL.CARRY_CAP", { unit: meterValues?.[index]?.unitLabel || t("STEP_PRICING_MODEL.UNITS_FALLBACK") })}
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
                        {t("STEP_PRICING_MODEL.CARRY_CAP_HINT")}
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
                    <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.AGGREGATION")}</FormLabel>
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
                          {t("STEP_PRICING_MODEL.ALLOW_OVERAGE")}
                        </FormLabel>
                      </div>
                    </FormItem>
                  )}
                />
              ) : (
                <p className="text-xs text-muted-foreground">
                  {t("STEP_PRICING_MODEL.LIFETIME_NO_OVERAGE")}
                </p>
              )}
              <FormField
                control={control}
                name={`meters.${index}.thresholdPercents`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.NOTIFY_AT")}</FormLabel>
                    <ThresholdChipInput value={inputField.value} onChange={inputField.onChange} />
                  </FormItem>
                )}
              />

              {/* Only meaningful where overage is permitted: a blocked meter never produces
                    billable units, so there is nothing to price. */}
              {meterValues?.[index]?.overageAllowed ? (
                <FormItem>
                  <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.OVERAGE_PRICING")}</FormLabel>
                  {meterValues[index]?.rateTables?.length ? null : (
                    <p className="text-xs text-muted-foreground">
                      {t("STEP_PRICING_MODEL.NO_OVERAGE_PRICE")}
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
                  <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.ALLOWANCE_PERIOD")}</FormLabel>
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
                    {t("STEP_PRICING_MODEL.ALLOWANCE_PERIOD_HINT")}
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
                  <FormLabel className="text-xs">{t("STEP_PRICING_MODEL.HOW_MANY")}</FormLabel>
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
        <h3 className="text-sm font-semibold">{t("STEP_PRICING_MODEL.PAYMENT_METHOD")}</h3>
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
                  {t("STEP_PRICING_MODEL.REQUIRE_PAYMENT_METHOD")}
                </FormLabel>
              </div>
              <p className="text-xs text-muted-foreground">
                {requirePaymentMethodUpfront
                  ? t("STEP_PRICING_MODEL.PAYMENT_METHOD_HINT_ON")
                  : t("STEP_PRICING_MODEL.PAYMENT_METHOD_HINT_OFF")}
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
 *
 * Only the rule that applies is shown. Both at once reads as a choice the author has to make, when
 * it is not one — the price they have already chosen decides it, and the other half is noise they
 * have to work out is irrelevant before they can act on the half that is not.
 */
const PlacesExplanation = ({ mode }: { mode: PlacesMode }) => {
  const { t } = useT("utilities.subscription.plan-builder");

  return (
    <div className="space-y-2 rounded-md bg-muted/60 p-3 text-xs leading-relaxed text-muted-foreground">
      <p>{t("STEP_PRICING_MODEL.PLACES_INTRO")}</p>

      {mode === "flat" ? null : (
        <p>
          <RichText text={t("STEP_PRICING_MODEL.PLACES_PER_UNIT")} />
        </p>
      )}

      {mode === "perPlace" ? null : (
        <p>
          <RichText text={t("STEP_PRICING_MODEL.PLACES_FLAT")} />
        </p>
      )}
    </div>
  );
};

type PlacesMode = "unknown" | "perPlace" | "flat";

/**
 * How this plan's places will be counted, as far as the prices authored so far reveal.
 *
 * Any price not sitting on the counting quantity makes a maximum necessary, because a buyer who
 * chooses that price is charged the same however many people they have — so the quantity they
 * bought cannot be the number of places. Before any price exists there is nothing to go on, and
 * both rules are shown rather than a guess.
 */
const placesModeOf = (
  quantityItems: { itemKey?: string; countsMembers?: boolean }[] | undefined,
  prices: { quantityItemKey?: string }[] | undefined,
): PlacesMode => {
  const items = quantityItems ?? [];
  const counting = items.length === 1 ? items[0] : items.find((item) => item?.countsMembers);

  if (!counting?.itemKey || (prices ?? []).length === 0) {
    return "unknown";
  }

  return (prices ?? []).every((price) => price?.quantityItemKey === counting.itemKey)
    ? "perPlace"
    : "flat";
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
