import { useFieldArray, useFormContext, useWatch } from "react-hook-form";
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
  BILLING_INTERVAL_OPTIONS,
  SUBSCRIPTION_CURRENCY_OPTIONS,
} from "../../constants/subscription.constants";
import type { PlanPrice } from "../../models/subscription-plan.model";
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import {
  defaultSubscriptionPriceFormValues,
  FLAT_FEE,
} from "../../schemas/subscription-price.schema";
import { formatPrice } from "../../utilities/subscription-format";
import { CardListItem, CardListShell } from "./card-list-shell";

/**
 * The prices the plan will be sold on. A repeatable list rather than one price, because the
 * ordinary case is more than one — a monthly and an annual price are two prices on the same plan,
 * and so is the same plan sold in two currencies.
 */
export const PlanPriceFields = ({
  isEditing = false,
  existingPrices = [],
  onRetirePrice,
  retiringPriceId = null,
}: {
  isEditing?: boolean;
  /**
   * The prices this plan already has, shown read-only. Editing them is not offered because the
   * API has no endpoint for it — a price is referenced by every subscription sold on it, so it
   * is added and superseded rather than changed. Showing them is still necessary: an author who
   * cannot see the monthly price already there is the one who adds a second.
   */
  existingPrices?: PlanPrice[];
  /** Omitted when nothing can be retired — creating a plan, or no price on it yet. */
  onRetirePrice?: (priceId: string) => void;
  retiringPriceId?: string | null;
}) => {
  const { control, formState } = useFormContext<CreateSubscriptionPlanFormValues>();
  const prices = useFieldArray({ control, name: "prices" });
  const quantityItems = useWatch({ control, name: "quantityItems" });

  // The "add at least one" issue lands on the array itself, which no per-field FormMessage
  // renders — the same trap the rate-table tier ordering hit.
  const listError = formState.errors.prices?.message ?? formState.errors.prices?.root?.message;

  return (
    <div className="space-y-3">
      <div>
        <h3 className="text-sm font-semibold">
          {isEditing ? "Add a price" : "How much does it cost?"}
        </h3>
        <p className="mt-1 text-xs text-muted-foreground">
          {isEditing
            ? "Prices are separate from the plan's terms, so the ones it already has are left alone. Anything added here is created alongside your edit."
            : "The recurring charge itself, separate from any overage above. Add one per billing cadence you sell — monthly and annually are two prices."}
        </p>
      </div>

      {existingPrices.length > 0 && (
        <div className="rounded-lg border border-border/70 bg-muted/40 p-3">
          <p className="text-xs font-medium text-muted-foreground">
            Already on this plan
          </p>
          <ul className="mt-2 space-y-1">
            {existingPrices.map((price) => (
              <li
                key={price.priceId}
                className="flex items-center justify-between gap-3 text-sm"
              >
                <span>{formatPrice(price)}</span>
                {onRetirePrice && (
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    disabled={retiringPriceId !== null}
                    onClick={() => onRetirePrice(price.priceId)}
                    className="h-7 shrink-0 text-xs text-muted-foreground hover:text-destructive"
                  >
                    {retiringPriceId === price.priceId ? "Retiring…" : "Retire"}
                  </Button>
                )}
              </li>
            ))}
          </ul>
          <p className="mt-2 text-xs text-muted-foreground">
            Retiring stops a price being sold. Anyone already on it keeps their terms and their
            renewals — a subscription bills from what it was sold on, not from this list. Prices
            are never edited or deleted, only superseded.
          </p>
        </div>
      )}

      <CardListShell
        addLabel="Add another price"
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
                    <FormLabel className="text-xs">Currency</FormLabel>
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
              <FormField
                control={control}
                name={`prices.${index}.amount`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">Amount</FormLabel>
                    <FormControl>
                      <Input {...inputField} type="number" min={0} step="0.01" placeholder="89.00" />
                    </FormControl>
                    <FormDescription className="text-xs">
                      In major units — 89.00, not 8900.
                    </FormDescription>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>

            <div className="grid grid-cols-2 gap-2">
              <FormField
                control={control}
                name={`prices.${index}.interval`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">Billed every</FormLabel>
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
                    <FormLabel className="text-xs">How many</FormLabel>
                    <FormControl>
                      <Input {...inputField} type="number" min={1} max={36} />
                    </FormControl>
                    <FormDescription className="text-xs">
                      3 with &ldquo;month&rdquo; is quarterly.
                    </FormDescription>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>

            <FormField
              control={control}
              name={`prices.${index}.quantityItemKey`}
              render={({ field: inputField }) => (
                <FormItem>
                  <FormLabel className="text-xs">What this multiplies</FormLabel>
                  <Select value={inputField.value} onValueChange={inputField.onChange}>
                    <FormControl>
                      <SelectTrigger>
                        <SelectValue />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      <SelectItem value={FLAT_FEE}>Flat fee</SelectItem>
                      {(quantityItems ?? [])
                        .filter((item) => item.itemKey)
                        .map((item) => (
                          <SelectItem key={item.itemKey} value={item.itemKey}>
                            Per {item.unitLabel || item.itemKey}
                          </SelectItem>
                        ))}
                    </SelectContent>
                  </Select>
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
