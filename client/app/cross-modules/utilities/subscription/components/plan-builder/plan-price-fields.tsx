import { useT } from "@seliseblocks/genesis-os";
import { useFieldArray, useFormContext, useWatch } from "react-hook-form";
import { useState } from "react";
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import {
  BILLING_ALIGNMENT_OPTIONS,
  BILLING_INTERVAL_OPTIONS,
  CALENDAR_ANNUAL_CHARGE_TIMING_OPTIONS,
  SUBSCRIPTION_CURRENCY_OPTIONS,
  YEARLY_BILLING_ALIGNMENT_OPTIONS,
} from "../../constants/subscription.constants";
import type { PlanPrice } from "../../models/subscription-plan.model";
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import {
  defaultSubscriptionPriceFormValues,
  FLAT_FEE,
} from "../../schemas/subscription-price.schema";
import {
  calendarAlignmentExample,
  calendarYearlyAlignmentExample,
  isCalendarEligible,
  isCompatibleStubBasis,
  needsStubBasePrice,
  requiresStubBasePrice,
} from "../../utilities/billing-alignment";
import { formatMoney, formatPrice } from "../../utilities/subscription-format";
import {
  AUTOMATIC_DISCOUNT_COMBINATION_OPTIONS,
  describeAutomaticDiscount,
  type AutomaticDiscountCombination,
} from "../../utilities/subscription-discount";
import { describeTax, TAX_MODE_OPTIONS } from "../../utilities/subscription-tax";
import { CardListItem, CardListShell } from "./card-list-shell";

const UTILITIES_SUBSCRIPTION_PLAN_BUILDER_MODULE = "utilities.subscription.plan-builder";

const ExistingPriceTaxEditor = ({
  price,
  onSave,
}: {
  price: PlanPrice;
  onSave: (priceId: string, taxPercent?: number, taxMode?: "Exclusive" | "Inclusive") => Promise<void>;
}) => {
  const { t } = useT(UTILITIES_SUBSCRIPTION_PLAN_BUILDER_MODULE);
  const [taxPercent, setTaxPercent] = useState<string>(
    price.taxRateBasisPoints ? String(price.taxRateBasisPoints / 100) : "",
  );
  const [taxMode, setTaxMode] = useState<"Exclusive" | "Inclusive">(
    price.taxMode === "Inclusive" ? "Inclusive" : "Exclusive",
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const numericRate = taxPercent === "" ? undefined : Number(taxPercent);

  return (
    <div className="mt-2 grid gap-2 rounded-md border border-dashed p-2 sm:grid-cols-[8rem_13rem_auto]">
      <Input
        aria-label={t("PLAN_PRICE_FIELDS.TAX_RATE_FOR", { price: formatPrice(price) })}
        type="number"
        min={0}
        max={100}
        step="0.01"
        value={taxPercent}
        placeholder={t("PLAN_PRICE_FIELDS.NO_TAX")}
        onChange={(event) => setTaxPercent(event.target.value)}
      />
      <Select value={taxMode} onValueChange={(value) => setTaxMode(value as typeof taxMode)}>
        <SelectTrigger aria-label={t("PLAN_PRICE_FIELDS.TAX_MODE_FOR", { price: formatPrice(price) })} disabled={!numericRate}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {TAX_MODE_OPTIONS.map((option) => (
            <SelectItem key={option.value} value={option.value}>{option.label}</SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Button
        type="button"
        size="sm"
        disabled={saving || numericRate !== undefined && (!Number.isFinite(numericRate) || numericRate < 0 || numericRate > 100)}
        onClick={async () => {
          setSaving(true);
          setError(null);
          try {
            await onSave(price.priceId, numericRate, numericRate ? taxMode : undefined);
          } catch (reason) {
            setError(reason instanceof Error ? reason.message : t("PLAN_PRICE_FIELDS.TAX_SAVE_FAILED"));
          } finally {
            setSaving(false);
          }
        }}
      >
        {saving ? t("PLAN_PRICE_FIELDS.SAVING") : t("PLAN_PRICE_FIELDS.SAVE_TAX")}
      </Button>
      {error && <p className="text-xs text-destructive sm:col-span-3">{error}</p>}
    </div>
  );
};

/**
 * The automatic discount on a price that already exists.
 *
 * Its own editor beside the tax one, because they are two endpoints and two decisions: an author
 * changing a discount has not necessarily changed their mind about VAT. Both are the same deliberate
 * exception to a price being immutable — they reach future subscriptions only.
 */
const ExistingPriceDiscountEditor = ({
  price,
  onSave,
}: {
  price: PlanPrice;
  onSave: (
    priceId: string,
    discountPercent?: number,
    combination?: AutomaticDiscountCombination,
  ) => Promise<void>;
}) => {
  const { t } = useT(UTILITIES_SUBSCRIPTION_PLAN_BUILDER_MODULE);
  const [discountPercent, setDiscountPercent] = useState<string>(
    price.automaticDiscountBasisPoints ? String(price.automaticDiscountBasisPoints / 100) : "",
  );
  const [combination, setCombination] = useState<AutomaticDiscountCombination>(
    price.quantityDiscountCombination === "Additive" ? "Additive" : "BestDiscount",
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const numericDiscount = discountPercent === "" ? undefined : Number(discountPercent);

  return (
    <div className="mt-2 grid gap-2 rounded-md border border-dashed p-2 sm:grid-cols-[8rem_13rem_auto]">
      <Input
        aria-label={t("PLAN_PRICE_FIELDS.DISCOUNT_FOR", { price: formatPrice(price) })}
        type="number"
        min={0}
        max={100}
        step="0.01"
        value={discountPercent}
        placeholder={t("PLAN_PRICE_FIELDS.NO_DISCOUNT")}
        onChange={(event) => setDiscountPercent(event.target.value)}
      />
      <Select
        value={combination}
        onValueChange={(value) => setCombination(value as AutomaticDiscountCombination)}
      >
        <SelectTrigger
          aria-label={t("PLAN_PRICE_FIELDS.COMBINATION_FOR", { price: formatPrice(price) })}
          disabled={!numericDiscount}
        >
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {AUTOMATIC_DISCOUNT_COMBINATION_OPTIONS.map((option) => (
            <SelectItem key={option.value} value={option.value}>{option.label}</SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Button
        type="button"
        size="sm"
        disabled={
          saving ||
          (numericDiscount !== undefined &&
            (!Number.isFinite(numericDiscount) || numericDiscount < 0 || numericDiscount > 100))
        }
        onClick={async () => {
          setSaving(true);
          setError(null);
          try {
            await onSave(price.priceId, numericDiscount, numericDiscount ? combination : undefined);
          } catch (reason) {
            setError(reason instanceof Error ? reason.message : t("PLAN_PRICE_FIELDS.DISCOUNT_SAVE_FAILED"));
          } finally {
            setSaving(false);
          }
        }}
      >
        {saving ? t("PLAN_PRICE_FIELDS.SAVING") : t("PLAN_PRICE_FIELDS.SAVE_DISCOUNT")}
      </Button>
      {error && <p className="text-xs text-destructive sm:col-span-3">{error}</p>}
    </div>
  );
};

/**
 * One price's tax, and what it means in words.
 *
 * The preview is the point. "7.7%" against "145.00" is two different prices depending on one
 * selector, and an author cannot check their own work without seeing the arithmetic — so the same
 * split the server calculates is shown here, in the currency they chose.
 *
 * Its own component because it watches three fields of this row; watching them in the list body
 * would re-render every price card whenever any one of them changed.
 */
const PriceTaxFields = ({ index }: { index: number }) => {
  const { t } = useT(UTILITIES_SUBSCRIPTION_PLAN_BUILDER_MODULE);
  const { control } = useFormContext<CreateSubscriptionPlanFormValues>();
  const price = useWatch({ control, name: `prices.${index}` });

  const taxPercent =
    price?.taxPercent === undefined || price?.taxPercent === null
      ? undefined
      : Number(price.taxPercent);

  const preview = describeTax({
    // Both come off a number input, so both are strings until the resolver coerces a copy. Coerced
    // here too, or "145" + tax turns into string concatenation and the preview reads "1458.9".
    amount: price?.amount === undefined ? undefined : Number(price.amount),
    currencyCode: price?.currencyCode ?? "USD",
    taxPercent,
    taxMode: price?.taxMode ?? "Exclusive",
  });

  const taxable = Boolean(taxPercent && taxPercent > 0);

  return (
    <div className="space-y-2 rounded-md border border-dashed border-border/70 p-2">
      <p className="text-xs font-medium">{t("PLAN_PRICE_FIELDS.TAX_HEADING")}</p>

      <div className="grid grid-cols-2 gap-2">
        <FormField
          control={control}
          name={`prices.${index}.taxPercent`}
          render={({ field: inputField }) => (
            <FormItem>
              <FormLabel className="text-xs">{t("PLAN_PRICE_FIELDS.RATE_OPTIONAL")}</FormLabel>
              <FormControl>
                <Input
                  {...inputField}
                  // Never undefined: an input whose value goes from a number to undefined becomes
                  // uncontrolled mid-edit, and React keeps whatever was last typed on screen while
                  // the form holds nothing.
                  value={inputField.value ?? ""}
                  type="number"
                  min={0}
                  max={100}
                  step="0.01"
                  placeholder="7.7"
                  aria-label={t("PLAN_PRICE_FIELDS.TAX_RATE_FOR_PRICE", { number: index + 1 })}
                />
              </FormControl>
              <FormDescription className="text-xs">
                {t("PLAN_PRICE_FIELDS.RATE_HINT")}
              </FormDescription>
              <FormMessage />
            </FormItem>
          )}
        />

        {taxable && (
          <FormField
            control={control}
            name={`prices.${index}.taxMode`}
            render={({ field: inputField }) => (
              <FormItem>
                <FormLabel className="text-xs">{t("PLAN_PRICE_FIELDS.AMOUNT_ABOVE_IS")}</FormLabel>
                <Select value={inputField.value} onValueChange={inputField.onChange}>
                  <FormControl>
                    <SelectTrigger aria-label={t("PLAN_PRICE_FIELDS.TAX_MODE_FOR_PRICE", { number: index + 1 })}>
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
                <FormDescription className="text-xs">
                  {TAX_MODE_OPTIONS.find((option) => option.value === inputField.value)?.hint}
                </FormDescription>
                <FormMessage />
              </FormItem>
            )}
          />
        )}
      </div>

      {preview && (
        <p className="text-xs text-muted-foreground" data-testid={`tax-preview-${index}`}>
          {preview}
        </p>
      )}
    </div>
  );
};

/**
 * One price's automatic discount, and the whole calculation it takes part in.
 *
 * The preview is the point, and it is a different preview from the tax one: "8% off" and "5% for
 * volume" produce two different totals depending on one selector, and an author cannot check their
 * own work without seeing which. It prices the default quantity through the band that quantity
 * selects, so the sentence under the card is the charge a first subscriber would actually see.
 *
 * Its own component for the same reason the tax fields are: it watches this row and the plan's
 * quantity items, and watching those in the list body would re-render every price card on every
 * keystroke in any of them.
 */
const PriceDiscountFields = ({ index }: { index: number }) => {
  const { t } = useT(UTILITIES_SUBSCRIPTION_PLAN_BUILDER_MODULE);
  const { control } = useFormContext<CreateSubscriptionPlanFormValues>();
  const price = useWatch({ control, name: `prices.${index}` });
  const quantityItems = useWatch({ control, name: "quantityItems" });

  const discountPercent =
    price?.automaticDiscountPercent === undefined || price?.automaticDiscountPercent === null
      ? undefined
      : Number(price.automaticDiscountPercent);

  // The item this price multiplies, if any. A flat fee has no quantity and no band, and prices as
  // one unit — which is exactly what the preview should show for it.
  const item = (quantityItems ?? []).find(
    (candidate) => candidate.itemKey && candidate.itemKey === price?.quantityItemKey,
  );
  const quantity = item ? Number(item.defaultQuantity) || 1 : 1;
  const band = (item?.quantityDiscountTiers ?? []).find(
    (tier) =>
      quantity >= Number(tier.minimumQuantity) &&
      (tier.maximumQuantity === undefined || quantity <= Number(tier.maximumQuantity)),
  );

  const combination: AutomaticDiscountCombination =
    price?.quantityDiscountCombination === "Additive" ? "Additive" : "BestDiscount";

  const preview = describeAutomaticDiscount({
    // Coerced for the same reason the tax preview coerces: these come off number inputs as strings
    // until the resolver copies them, and "100" * 2 is fine while "100" + 8 is not.
    amount: price?.amount === undefined ? undefined : Number(price.amount),
    currencyCode: price?.currencyCode ?? "USD",
    quantity,
    automaticDiscountPercent: discountPercent,
    quantityDiscountPercent: band ? Number(band.discountPercent) : 0,
    combination,
    taxPercent:
      price?.taxPercent === undefined || price?.taxPercent === null
        ? undefined
        : Number(price.taxPercent),
    taxMode: price?.taxMode ?? "Exclusive",
  });

  const discounted = Boolean(discountPercent && discountPercent > 0);

  return (
    <div className="space-y-2 rounded-md border border-dashed border-border/70 p-2">
      <p className="text-xs font-medium">{t("PLAN_PRICE_FIELDS.DISCOUNT_HEADING")}</p>

      <div className="grid grid-cols-2 gap-2">
        <FormField
          control={control}
          name={`prices.${index}.automaticDiscountPercent`}
          render={({ field: inputField }) => (
            <FormItem>
              <FormLabel className="text-xs">{t("PLAN_PRICE_FIELDS.DISCOUNT_PERCENT")}</FormLabel>
              <FormControl>
                <Input
                  {...inputField}
                  // Never undefined, or the input goes uncontrolled mid-edit and keeps showing a
                  // number the form no longer holds.
                  value={inputField.value ?? ""}
                  type="number"
                  min={0}
                  max={100}
                  step="0.01"
                  placeholder="8"
                  aria-label={t("PLAN_PRICE_FIELDS.DISCOUNT_FOR_PRICE", { number: index + 1 })}
                />
              </FormControl>
              <FormDescription className="text-xs">
                {t("PLAN_PRICE_FIELDS.DISCOUNT_HINT")}
              </FormDescription>
              <FormMessage />
            </FormItem>
          )}
        />

        {discounted && (
          <FormField
            control={control}
            name={`prices.${index}.quantityDiscountCombination`}
            render={({ field: inputField }) => (
              <FormItem>
                <FormLabel className="text-xs">{t("PLAN_PRICE_FIELDS.COMBINE")}</FormLabel>
                <Select value={inputField.value} onValueChange={inputField.onChange}>
                  <FormControl>
                    <SelectTrigger aria-label={t("PLAN_PRICE_FIELDS.COMBINATION_FOR_PRICE", { number: index + 1 })}>
                      <SelectValue />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    {AUTOMATIC_DISCOUNT_COMBINATION_OPTIONS.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <FormDescription className="text-xs">
                  {
                    AUTOMATIC_DISCOUNT_COMBINATION_OPTIONS.find(
                      (option) => option.value === inputField.value,
                    )?.hint
                  }
                </FormDescription>
                <FormMessage />
              </FormItem>
            )}
          />
        )}
      </div>

      {preview && (
        <p className="text-xs text-muted-foreground" data-testid={`discount-preview-${index}`}>
          {preview}
        </p>
      )}
    </div>
  );
};

/**
 * What the price costs.
 *
 * Always the author's to set, on every cadence. A calendar-aligned yearly price is linked to a
 * monthly one, but that link prices the opening period only — what a year costs stays a commercial
 * decision, and an annual plan is usually not twelve monthly ones. Only the wording of the hint
 * changes, to say which of the two figures this one is.
 */
const PriceAmountField = ({ index }: { index: number }) => {
  const { t } = useT(UTILITIES_SUBSCRIPTION_PLAN_BUILDER_MODULE);
  const { control } = useFormContext<CreateSubscriptionPlanFormValues>();
  const price = useWatch({ control, name: `prices.${index}` });

  const yearlyCalendar = requiresStubBasePrice({
    interval: Number(price?.interval),
    intervalCount: Number(price?.intervalCount),
    billingAlignment: price?.billingAlignment,
  });

  return (
    <FormField
      control={control}
      name={`prices.${index}.amount`}
      render={({ field: inputField }) => (
        <FormItem>
          <FormLabel className="text-xs">{t("PLAN_PRICE_FIELDS.AMOUNT")}</FormLabel>
          <FormControl>
            <Input
              {...inputField}
              type="number"
              min={0}
              step="0.01"
              placeholder="89.00"
              aria-label={t("PLAN_PRICE_FIELDS.AMOUNT_FOR_PRICE", { number: index + 1 })}
            />
          </FormControl>
          <FormDescription className="text-xs">
            {yearlyCalendar
              ? t("PLAN_PRICE_FIELDS.AMOUNT_HINT_YEARLY")
              : t("PLAN_PRICE_FIELDS.AMOUNT_HINT")}
          </FormDescription>
          <FormMessage />
        </FormItem>
      )}
    />
  );
};

/**
 * When this price renews, for the one cadence that gets a choice.
 *
 * Hidden rather than disabled for every other cadence. A greyed-out "renew on the 1st" invites the
 * question of how to enable it, and the answer — change the cadence you are selling — is not a
 * setting on this control.
 *
 * Its own component because it watches two fields of this row; watching them in the list body
 * would re-render every price card whenever either changed.
 */
const PriceBillingAlignmentField = ({
  index,
  monthlyPrices,
}: {
  index: number;
  /** The plan's active monthly prices, which a yearly one can be charged from. */
  monthlyPrices: PlanPrice[];
}) => {
  const { control } = useFormContext<CreateSubscriptionPlanFormValues>();
  const price = useWatch({ control, name: `prices.${index}` });

  const { t } = useT(UTILITIES_SUBSCRIPTION_PLAN_BUILDER_MODULE);
  const cadence = {
    interval: Number(price?.interval),
    intervalCount: Number(price?.intervalCount),
  };

  if (!price || !isCalendarEligible(cadence)) {
    return null;
  }

  // A year and a month are the same mechanism and the same stored value, but not the same
  // sentence: "renew on the 1st" without saying of which month reads as monthly billing.
  const yearly = needsStubBasePrice(cadence);
  const options = yearly ? YEARLY_BILLING_ALIGNMENT_OPTIONS : BILLING_ALIGNMENT_OPTIONS;

  return (
    <>
      <FormField
        control={control}
        name={`prices.${index}.billingAlignment`}
        render={({ field: inputField }) => (
          <FormItem>
            <FormLabel className="text-xs">{t("PLAN_PRICE_FIELDS.BILLING_CYCLE")}</FormLabel>
            <Select value={inputField.value} onValueChange={inputField.onChange}>
              <FormControl>
                <SelectTrigger aria-label={t("PLAN_PRICE_FIELDS.BILLING_CYCLE_FOR_PRICE", { number: index + 1 })}>
                  <SelectValue />
                </SelectTrigger>
              </FormControl>
              <SelectContent>
                {options.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <FormDescription className="text-xs">
              {options.find((option) => option.value === inputField.value)?.hint}
            </FormDescription>
            {inputField.value === "CalendarMonth" && (
              <p
                className="text-xs text-muted-foreground"
                data-testid={`billing-alignment-example-${index}`}
              >
                {yearly ? calendarYearlyAlignmentExample() : calendarAlignmentExample()}
              </p>
            )}
            <FormMessage />
          </FormItem>
        )}
      />

      {yearly && price.billingAlignment === "CalendarMonth" && (
        <PriceStubBasisField index={index} monthlyPrices={monthlyPrices} />
      )}
    </>
  );
};

/**
 * The monthly price a calendar-aligned yearly price is charged from, and the annual figure that
 * follows from it.
 *
 * The annual amount is shown rather than entered, because the server derives it: an annual price
 * and its own monthly equivalent cannot be allowed to disagree about what a year costs, and only
 * one of the two can be the source of that truth. An editable field here would be overwritten,
 * which is worse than one that never invited the edit.
 *
 * Offers only prices the plan already has. A yearly price cannot be charged from a monthly one
 * being authored in the same submission, because that one has no id until it is created.
 */
const PriceStubBasisField = ({
  index,
  monthlyPrices,
}: {
  index: number;
  monthlyPrices: PlanPrice[];
}) => {
  const { control } = useFormContext<CreateSubscriptionPlanFormValues>();
  const price = useWatch({ control, name: `prices.${index}` });
  // Every rule the server applies, so the picker cannot offer a choice the API refuses.
  const { t } = useT(UTILITIES_SUBSCRIPTION_PLAN_BUILDER_MODULE);
  const eligible = monthlyPrices.filter((candidate) =>
    isCompatibleStubBasis(candidate, {
      currencyCode: price?.currencyCode ?? "",
      quantityItemKey: price?.quantityItemKey,
      taxPercent:
        price?.taxPercent === undefined || price?.taxPercent === null
          ? undefined
          : Number(price.taxPercent),
      taxMode: price?.taxMode,
    }),
  );

  const basis = eligible.find(
    (candidate) => candidate.priceId === price?.calendarStubBasePriceId,
  );

  return (
    <div className="space-y-2 rounded-md border border-dashed border-border/70 p-2">
      <p className="text-xs font-medium">{t("PLAN_PRICE_FIELDS.CHARGED_FROM")}</p>

      {eligible.length === 0 ? (
        <p className="text-xs text-muted-foreground" data-testid={`stub-basis-empty-${index}`}>
          {t("PLAN_PRICE_FIELDS.NO_COMPATIBLE_MONTHLY", { currency: price?.currencyCode ?? "" })}
        </p>
      ) : (
        <>
          <FormField
            control={control}
            name={`prices.${index}.calendarStubBasePriceId`}
            render={({ field: inputField }) => (
              <FormItem>
                <FormLabel className="text-xs">{t("PLAN_PRICE_FIELDS.MONTHLY_PRICE")}</FormLabel>
                <Select value={inputField.value ?? ""} onValueChange={inputField.onChange}>
                  <FormControl>
                    <SelectTrigger aria-label={t("PLAN_PRICE_FIELDS.MONTHLY_PRICE_FOR_PRICE", { number: index + 1 })}>
                      <SelectValue placeholder={t("PLAN_PRICE_FIELDS.CHOOSE_MONTHLY_PRICE")} />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    {eligible.map((candidate) => (
                      <SelectItem key={candidate.priceId} value={candidate.priceId}>
                        {formatPrice(candidate)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <FormDescription className="text-xs">
                  {t("PLAN_PRICE_FIELDS.OPENING_PERIOD_HINT")}
                </FormDescription>
                <FormMessage />
              </FormItem>
            )}
          />

          {basis && (
            <p
              className="text-xs text-muted-foreground"
              data-testid={`stub-basis-preview-${index}`}
            >
              {t("PLAN_PRICE_FIELDS.STUB_PREVIEW", { amount: formatMoney(basis.unitAmountMinor, basis.currencyCode) })}
            </p>
          )}

          <FormField
            control={control}
            name={`prices.${index}.calendarAnnualChargeTiming`}
            render={({ field: inputField }) => (
              <FormItem>
                <FormLabel className="text-xs">{t("PLAN_PRICE_FIELDS.COLLECT_YEAR")}</FormLabel>
                <Select value={inputField.value} onValueChange={inputField.onChange}>
                  <FormControl>
                    <SelectTrigger
                      aria-label={t("PLAN_PRICE_FIELDS.ANNUAL_TIMING_FOR_PRICE", { number: index + 1 })}
                    >
                      <SelectValue />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    {CALENDAR_ANNUAL_CHARGE_TIMING_OPTIONS.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <FormDescription className="text-xs">
                  {
                    CALENDAR_ANNUAL_CHARGE_TIMING_OPTIONS.find(
                      (option) => option.value === inputField.value,
                    )?.hint
                  }
                </FormDescription>
                <FormMessage />
              </FormItem>
            )}
          />
        </>
      )}
    </div>
  );
};

/**
 * The prices the plan will be sold on. A repeatable list rather than one price, because the
 * ordinary case is more than one — a monthly and an annual price are two prices on the same plan,
 * and so is the same plan sold in two currencies.
 */
export const PlanPriceFields = ({
  isEditing = false,
  existingPrices = [],
  onRetirePrice,
  onUpdatePriceTax,
  onUpdatePriceDiscount,
  retiringPriceId = null,
}: {
  isEditing?: boolean;
  /**
   * The prices this plan already has. Commercial terms stay immutable because every subscription
   * sold on a price references them; tax metadata is the deliberate exception and affects only
   * future snapshots. Showing the full list also prevents authors accidentally adding a duplicate.
   */
  existingPrices?: PlanPrice[];
  /** Omitted when nothing can be retired — creating a plan, or no price on it yet. */
  onRetirePrice?: (priceId: string) => void;
  onUpdatePriceTax?: (
    priceId: string,
    taxPercent?: number,
    taxMode?: "Exclusive" | "Inclusive",
  ) => Promise<void>;
  onUpdatePriceDiscount?: (
    priceId: string,
    discountPercent?: number,
    combination?: AutomaticDiscountCombination,
  ) => Promise<void>;
  retiringPriceId?: string | null;
}) => {
  const { t } = useT(UTILITIES_SUBSCRIPTION_PLAN_BUILDER_MODULE);
  const { control, formState } = useFormContext<CreateSubscriptionPlanFormValues>();
  const prices = useFieldArray({ control, name: "prices" });
  const quantityItems = useWatch({ control, name: "quantityItems" });

  // Only prices the plan already has: one being authored in the same submission has no id yet, so
  // nothing could be linked to it.
  const monthlyPrices = existingPrices.filter(
    (price) => price.interval === "Month" && price.intervalCount === 1,
  );

  // The "add at least one" issue lands on the array itself, which no per-field FormMessage
  // renders — the same trap the rate-table tier ordering hit.
  const listError = formState.errors.prices?.message ?? formState.errors.prices?.root?.message;

  return (
    <div className="space-y-3">
      <div>
        <h3 className="text-sm font-semibold">
          {isEditing ? t("PLAN_PRICE_FIELDS.HEADING_EDIT") : t("PLAN_PRICE_FIELDS.HEADING")}
        </h3>
        <p className="mt-1 text-xs text-muted-foreground">
          {isEditing
            ? t("PLAN_PRICE_FIELDS.HINT_EDIT")
            : t("PLAN_PRICE_FIELDS.HINT")}
        </p>
      </div>

      {existingPrices.length > 0 && (
        <div className="rounded-lg border border-border/70 bg-muted/40 p-3">
          <p className="text-xs font-medium text-muted-foreground">
            {t("PLAN_PRICE_FIELDS.ALREADY_ON_PLAN")}
          </p>
          <ul className="mt-2 space-y-1">
            {existingPrices.map((price) => (
              <li
                key={price.priceId}
                className="text-sm"
              >
                <div className="flex items-center justify-between gap-3">
                  <span>
                    {formatPrice(price)}
                    {price.displayPriceNote && <span className="ml-2 text-xs text-muted-foreground">{price.displayPriceNote}</span>}
                  </span>
                  {onRetirePrice && (
                    <Button type="button" variant="ghost" size="sm" disabled={retiringPriceId !== null}
                      onClick={() => onRetirePrice(price.priceId)} className="h-7 shrink-0 text-xs text-muted-foreground hover:text-destructive">
                      {retiringPriceId === price.priceId
                        ? t("PLAN_PRICE_FIELDS.RETIRING")
                        : t("PLAN_PRICE_FIELDS.RETIRE")}
                    </Button>
                  )}
                </div>
                {onUpdatePriceTax && <ExistingPriceTaxEditor price={price} onSave={onUpdatePriceTax} />}
                {onUpdatePriceDiscount && (
                  <ExistingPriceDiscountEditor price={price} onSave={onUpdatePriceDiscount} />
                )}
              </li>
            ))}
          </ul>
          <p className="mt-2 text-xs text-muted-foreground">
            {t("PLAN_PRICE_FIELDS.RETIRE_HINT")}
          </p>
        </div>
      )}

      <CardListShell
        addLabel={t("PLAN_PRICE_FIELDS.ADD_PRICE")}
        onAdd={() => prices.append({ ...defaultSubscriptionPriceFormValues })}
      >
        {prices.fields.map((field, index) => (
          <CardListItem key={field.id} onRemove={() => prices.remove(index)}>
            <div className="grid grid-cols-2 gap-2">
              <FormField
                control={control}
                name={`prices.${index}.currencyCode`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">{t("PLAN_PRICE_FIELDS.CURRENCY")}</FormLabel>
                    <Select value={inputField.value} onValueChange={inputField.onChange}>
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
              <PriceAmountField index={index} />
            </div>

            <div className="grid grid-cols-2 gap-2">
              <FormField
                control={control}
                name={`prices.${index}.interval`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">{t("PLAN_PRICE_FIELDS.BILLED_EVERY")}</FormLabel>
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
                name={`prices.${index}.intervalCount`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">{t("PLAN_PRICE_FIELDS.HOW_MANY")}</FormLabel>
                    <FormControl>
                      <Input {...inputField} type="number" min={1} max={36} />
                    </FormControl>
                    <FormDescription className="text-xs">
                      {t("PLAN_PRICE_FIELDS.QUARTERLY_HINT")}
                    </FormDescription>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>

            <PriceBillingAlignmentField index={index} monthlyPrices={monthlyPrices} />

            <FormField
              control={control}
              name={`prices.${index}.displayPriceNote`}
              render={({ field: inputField }) => (
                <FormItem>
                  <FormLabel className="text-xs">{t("PLAN_PRICE_FIELDS.DISPLAY_NOTE")}</FormLabel>
                  <FormControl>
                    <Input {...inputField} placeholder={t("PLAN_PRICE_FIELDS.DISPLAY_NOTE_PLACEHOLDER")} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <PriceTaxFields index={index} />

            <PriceDiscountFields index={index} />

            <FormField
              control={control}
              name={`prices.${index}.quantityItemKey`}
              render={({ field: inputField }) => (
                <FormItem>
                  <FormLabel className="text-xs">{t("PLAN_PRICE_FIELDS.MULTIPLIES")}</FormLabel>
                  <Select value={inputField.value} onValueChange={inputField.onChange}>
                    <FormControl>
                      <SelectTrigger>
                        <SelectValue />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      <SelectItem value={FLAT_FEE}>{t("PLAN_PRICE_FIELDS.FLAT_FEE")}</SelectItem>
                      {(quantityItems ?? [])
                        .filter((item) => item.itemKey)
                        .map((item) => (
                          <SelectItem key={item.itemKey} value={item.itemKey}>
                            {t("PLAN_PRICE_FIELDS.PER_ITEM", { item: item.unitLabel || item.itemKey })}
                          </SelectItem>
                        ))}
                    </SelectContent>
                  </Select>
                  <FormDescription className="text-xs">
                    {t("PLAN_PRICE_FIELDS.MULTIPLIES_HINT")}
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />
          </CardListItem>
        ))}
      </CardListShell>

      {listError && (
        <p className="text-sm text-destructive" role="alert">
          {listError}
        </p>
      )}
    </div>
  );
};
