import { z } from "zod";

/**
 * Radix rejects an empty string as a SelectItem value, so "flat fee, not tied to a quantity
 * item" needs a sentinel. It never leaves this file: submit maps it back to an omitted field.
 */
export const FLAT_FEE = "__flat_fee__";

export const createSubscriptionPriceSchema = z.object({
  currencyCode: z
    .string()
    .trim()
    .length(3, "Use a three-letter currency code.")
    .toUpperCase(),
  // Entered in major units in the UI (e.g. "89.00") and converted to minor units on submit —
  // the API never wants a decimal, since the exponent belongs to the currency, not the request.
  amount: z.coerce.number().min(0, "Enter an amount of zero or more."),
  interval: z.coerce.number().int().min(0).max(3),
  intervalCount: z.coerce.number().int().min(1).max(36),
  quantityItemKey: z.string().min(1),
});

export type CreateSubscriptionPriceFormValues = z.infer<typeof createSubscriptionPriceSchema>;

export const defaultSubscriptionPriceFormValues: CreateSubscriptionPriceFormValues = {
  currencyCode: "USD",
  amount: 0,
  interval: 2,
  intervalCount: 1,
  quantityItemKey: FLAT_FEE,
};
