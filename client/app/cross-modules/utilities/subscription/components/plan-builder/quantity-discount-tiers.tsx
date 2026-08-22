import { Plus, X } from "lucide-react";
import { useFieldArray, useFormContext, useWatch } from "react-hook-form";
import { Button } from "@/components/ui-kits/button/button";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import {
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import { formatMoney, toMinorUnits } from "../../utilities/subscription-format";

/**
 * The volume bands on one quantity item.
 *
 * Volume pricing, not graduated pricing, and the difference is the whole reason this needs
 * explaining on screen: the band is chosen by the total quantity and its discount comes off the
 * entire charge. Ten users in a 10% band is ten users at 10% off — not four at full price and six
 * discounted. Authors who assume the graduated reading price their catalogue wrong and only find
 * out from an invoice.
 */
export const QuantityDiscountTiers = ({ itemIndex }: { itemIndex: number }) => {
  const { control, setValue } = useFormContext<CreateSubscriptionPlanFormValues>();

  const tiers = useFieldArray({
    control,
    name: `quantityItems.${itemIndex}.quantityDiscountTiers`,
  });

  // Watched rather than read from useFieldArray's snapshot, which only refreshes when the list
  // itself changes — the same trap the rate-table editor documents.
  const tierValues = useWatch({ control, name: `quantityItems.${itemIndex}.quantityDiscountTiers` });
  const item = useWatch({ control, name: `quantityItems.${itemIndex}` });
  const prices = useWatch({ control, name: "prices" });

  const enabled = tiers.fields.length > 0;

  // Only shown when exactly one price is written against this item: with two, "the" effective
  // price is a fiction, and quietly picking one of them is worse than showing none.
  const price = prices?.filter((candidate) => candidate?.quantityItemKey === item?.itemKey);
  const singlePrice = price?.length === 1 ? price[0] : undefined;

  const toggle = (next: boolean) => {
    if (next) {
      // Seeded as a working pair rather than one row: a single band is a flat discount, which the
      // unit price already expresses, and the schema refuses it.
      setValue(
        `quantityItems.${itemIndex}.quantityDiscountTiers`,
        [
          {
            minimumQuantity: item?.minQuantity ?? 1,
            maximumQuantity: (item?.minQuantity ?? 1) + 4,
            discountPercent: 0,
          },
          {
            minimumQuantity: (item?.minQuantity ?? 1) + 5,
            maximumQuantity: item?.maxQuantity,
            discountPercent: 5,
          },
        ],
        { shouldValidate: false },
      );

      return;
    }

    const authored = (tierValues ?? []).some((tier) => (tier?.discountPercent ?? 0) > 0);

    // Only asked when there is something to lose. A confirm on every toggle trains people to
    // dismiss it, which is how the one that mattered gets dismissed too.
    if (
      authored &&
      !window.confirm("Remove the volume bands on this item? The discounts you entered are lost.")
    ) {
      return;
    }

    setValue(`quantityItems.${itemIndex}.quantityDiscountTiers`, [], { shouldValidate: false });
  };

  return (
    <div className="space-y-3">
      <label className="flex items-center gap-2 text-sm">
        <Checkbox
          checked={enabled}
          onCheckedChange={(checked) => toggle(checked === true)}
          aria-label="Apply volume discounts"
        />
        Apply volume discounts
      </label>

      {enabled ? (
        <div className="space-y-3 rounded-md border border-dashed p-3">
          <div className="grid grid-cols-[1fr_1fr_1fr_auto] items-center gap-2 text-xs text-muted-foreground">
            <span>From</span>
            <span>To</span>
            <span>Discount</span>
            <span className="sr-only">Remove</span>
          </div>

          {tiers.fields.map((tier, tierIndex) => {
            const isLast = tierIndex === tiers.fields.length - 1;
            const values = tierValues?.[tierIndex];

            return (
              <div key={tier.id} className="space-y-1">
                <div className="grid grid-cols-[1fr_1fr_1fr_auto] items-start gap-2">
                  <FormField
                    control={control}
                    name={`quantityItems.${itemIndex}.quantityDiscountTiers.${tierIndex}.minimumQuantity`}
                    render={({ field }) => (
                      <FormItem>
                        <FormControl>
                          <Input
                            {...field}
                            type="number"
                            min={1}
                            aria-label={`Band ${tierIndex + 1} from`}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    control={control}
                    name={`quantityItems.${itemIndex}.quantityDiscountTiers.${tierIndex}.maximumQuantity`}
                    render={({ field }) => (
                      <FormItem>
                        <FormControl>
                          <Input
                            {...field}
                            value={field.value ?? ""}
                            type="number"
                            min={1}
                            placeholder={isLast ? "no limit" : ""}
                            aria-label={`Band ${tierIndex + 1} to`}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    control={control}
                    name={`quantityItems.${itemIndex}.quantityDiscountTiers.${tierIndex}.discountPercent`}
                    render={({ field }) => (
                      <FormItem>
                        <FormControl>
                          <Input
                            {...field}
                            type="number"
                            min={0}
                            max={100}
                            step="0.01"
                            aria-label={`Band ${tierIndex + 1} discount percent`}
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
                    disabled={tiers.fields.length <= 2}
                    aria-label={`Remove band ${tierIndex + 1}`}
                    className="text-muted-foreground hover:text-destructive"
                  >
                    <X className="h-4 w-4" />
                  </Button>
                </div>

                {singlePrice && values ? (
                  <p className="text-xs text-muted-foreground">
                    {effectivePrice(singlePrice, values.discountPercent)} per {item?.unitLabel || "unit"}
                  </p>
                ) : null}
              </div>
            );
          })}

          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => {
              const lastIndex = tiers.fields.length - 1;
              const last = tierValues?.[lastIndex];

              // The band being split is the open one, so it has no bound of its own to build on.
              // Derived from where it starts, which keeps the list contiguous — a constant put two
              // bands on the same boundary as soon as this was clicked twice.
              const boundary = (last?.minimumQuantity ?? item?.minQuantity ?? 1) + 4;

              tiers.update(lastIndex, {
                minimumQuantity: last?.minimumQuantity ?? item?.minQuantity ?? 1,
                maximumQuantity: boundary,
                discountPercent: last?.discountPercent ?? 0,
              });
              tiers.append({
                minimumQuantity: boundary + 1,
                // Left open when the item is unbounded, which is the only shape the schema
                // accepts for a final band there.
                maximumQuantity: item?.maxQuantity,
                discountPercent: last?.discountPercent ?? 0,
              });
            }}
          >
            <Plus className="mr-2 h-4 w-4" />
            Add band
          </Button>

          <p className="text-xs text-muted-foreground">
            The band is chosen by the total quantity, and its discount applies to the whole charge —
            not only to the units inside the band. A discount code, if one is used, combines
            according to the plan&apos;s combination policy.
          </p>
        </div>
      ) : null}
    </div>
  );
};

/**
 * What one unit costs inside a band. Presentation only — the server prices and rounds the charge,
 * and showing a figure derived here as though it were authoritative is how a catalogue comes to
 * disagree with its own invoices.
 */
const effectivePrice = (
  price: { amount?: number; currencyCode?: string },
  discountPercent: number,
): string => {
  const currency = price.currencyCode || "USD";
  // The builder edits prices in major units; formatMoney speaks minor, and the exponent belongs
  // to the currency rather than to either of them.
  const minor = toMinorUnits(price.amount ?? 0, currency);
  const discounted = Math.round(minor * (1 - (discountPercent || 0) / 100));

  return formatMoney(discounted, currency);
};
