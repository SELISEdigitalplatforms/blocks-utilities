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

  // Normalized, not trusted. A number input under react-hook-form holds whatever was typed —
  // the inferred type says number because zod coerces at the resolver, which runs later and
  // returns a copy. Read raw, "3" + 4 is "34" and a boundary lands three hundred quantities
  // above where the author meant.
  const minQuantity = count(item?.minQuantity) ?? 1;
  const maxQuantity = count(item?.maxQuantity);

  // An item whose whole range is one quantity cannot hold two bands, and two is the fewest that
  // means anything. Offered anyway, the control opened straight into a configuration the schema
  // refuses — 1-5 followed by 6-5 on an item capped at five.
  const span = maxQuantity === undefined ? Infinity : maxQuantity - minQuantity + 1;
  const canBand = span >= 2;

  // The last band splits only while it has room for two quantities. Left enabled past that, the
  // button produced a band starting above where it ends.
  const lastBand = band(tierValues?.[tiers.fields.length - 1]);
  const canAddBand =
    lastBand === undefined ||
    lastBand.maximumQuantity === undefined ||
    // Compared as numbers: as the strings they arrive as, "9" is greater than "10" and the
    // control closes on a band with plenty of room left in it.
    lastBand.maximumQuantity > lastBand.minimumQuantity;

  // Only shown when exactly one price is written against this item: with two, "the" effective
  // price is a fiction, and quietly picking one of them is worse than showing none.
  const price = prices?.filter((candidate) => candidate?.quantityItemKey === item?.itemKey);
  const singlePrice = price?.length === 1 ? price[0] : undefined;

  const toggle = (next: boolean) => {
    if (next) {
      // Seeded as a working pair rather than one row: a single band is a flat discount, which the
      // unit price already expresses, and the schema refuses it. The boundary is derived from the
      // room the item actually has, so a narrow item opens valid rather than opening broken.
      const boundary = firstBoundary(minQuantity, maxQuantity);

      setValue(
        `quantityItems.${itemIndex}.quantityDiscountTiers`,
        [
          {
            minimumQuantity: minQuantity,
            maximumQuantity: boundary,
            discountPercent: 0,
          },
          {
            minimumQuantity: boundary + 1,
            maximumQuantity: maxQuantity,
            discountPercent: 5,
          },
        ],
        { shouldValidate: false },
      );

      return;
    }

    const authored = (tierValues ?? []).some((tier) => (count(tier?.discountPercent) ?? 0) > 0);

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
          disabled={!canBand && !enabled}
          onCheckedChange={(checked) => toggle(checked === true)}
          aria-label="Apply volume discounts"
        />
        Apply volume discounts
      </label>

      {!canBand && !enabled ? (
        <p className="text-xs text-muted-foreground">
          This item allows only {span <= 0 ? "no" : "one"} quantity, so there is nothing to band.
          Raise its maximum to offer a volume discount.
        </p>
      ) : null}

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
            disabled={!canAddBand}
            onClick={() => {
              const lastIndex = tiers.fields.length - 1;
              const last = band(tierValues?.[lastIndex]);
              const start = last?.minimumQuantity ?? minQuantity;

              // The band being split is the last one, and when the item is unbounded it has no
              // bound of its own to build on. Derived from where it starts and from the room left
              // above it, which keeps the list contiguous and inside the item — a constant put two
              // bands on the same boundary as soon as this was clicked twice.
              const boundary = firstBoundary(start, last?.maximumQuantity ?? maxQuantity);

              tiers.update(lastIndex, {
                minimumQuantity: start,
                maximumQuantity: boundary,
                discountPercent: count(last?.discountPercent) ?? 0,
              });
              tiers.append({
                minimumQuantity: boundary + 1,
                // Keeps whatever ceiling the band being split had: the item's maximum on a bounded
                // item, and open on an unbounded one, which is the only shape the schema accepts
                // for a final band there.
                maximumQuantity: last?.maximumQuantity ?? maxQuantity,
                discountPercent: count(last?.discountPercent) ?? 0,
              });
            }}
          >
            <Plus className="mr-2 h-4 w-4" />
            Add band
          </Button>

          {!canAddBand ? (
            <p className="text-xs text-muted-foreground">
              The last band covers a single quantity, so it cannot be split again.
            </p>
          ) : null}

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
 * A watched numeric field as a number, or nothing at all.
 *
 * An emptied optional input holds <code>""</code> rather than <code>undefined</code>, and an
 * unbounded item read that way looked like an item with no room: <code>"" - 3 + 1</code> is
 * negative, so the control disabled itself on a plan that could band perfectly well.
 */
const count = (value: unknown): number | undefined => {
  if (value === "" || value === null || value === undefined) {
    return undefined;
  }

  const parsed = Number(value);

  return Number.isFinite(parsed) ? parsed : undefined;
};

/** One band with its bounds normalized, for the arithmetic that decides the next one. */
const band = (
  tier: { minimumQuantity?: unknown; maximumQuantity?: unknown; discountPercent?: unknown } | undefined,
):
  | { minimumQuantity: number; maximumQuantity: number | undefined; discountPercent: unknown }
  | undefined =>
  tier === undefined
    ? undefined
    : {
        minimumQuantity: count(tier.minimumQuantity) ?? 1,
        maximumQuantity: count(tier.maximumQuantity),
        discountPercent: tier.discountPercent,
      };

/**
 * Where to close the first of two bands covering everything from <code>from</code> upwards.
 *
 * Five quantities where there is room, and half the range where there is not. The constant alone
 * produced a first band wider than the item it belonged to — 1-5 on an item capped at four — and a
 * second band starting above where it ended.
 */
const firstBoundary = (from: number, upTo: number | undefined): number => {
  if (upTo === undefined) {
    return from + 4;
  }

  // At least one quantity has to be left for the band above, so the boundary stops one short of
  // the ceiling however narrow the range is.
  return Math.min(from + 4, upTo - 1);
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
