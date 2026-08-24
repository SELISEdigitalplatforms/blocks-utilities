import { z } from "zod";

import { TAX_MODES } from "../utilities/subscription-tax";

/**
 * Radix rejects an empty string as a SelectItem value, so "flat fee, not tied to a quantity
 * item" needs a sentinel. It never leaves this file: submit maps it back to an omitted field.
 */
export const FLAT_FEE = "__flat_fee__";

/**
 * One price's own fields, shared by the standalone add-price form and the repeatable price list
 * inside the plan builder — a plan usually sells on more than one of these (monthly and annually
 * are two prices), and both places have to describe a price the same way.
 */
export const subscriptionPriceFieldsSchema = z.object({
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
  displayPriceNote: z.string().trim().max(200).optional().or(z.literal("")),
  quantityItemKey: z.string().min(1),
  /**
   * A percentage, two decimal places, converted to basis points on submit. Optional: most prices
   * carry no tax at all, and a required field would make every author answer a question about VAT
   * to sell a plan in a country that has none.
   *
   * The empty string is a real value here, not a mistake. A number input that has been cleared
   * holds `""`, and coercing that to a number gives 0 — which would author a zero-rate tax rather
   * than no tax.
   */
  taxPercent: z.preprocess(
    (value) => (value === "" || value === null || value === undefined ? undefined : value),
    z.coerce
      .number()
      .min(0, "A tax rate cannot be negative.")
      .max(100, "A tax rate cannot exceed 100%.")
      .optional(),
  ),
  /**
   * Which reading of the amount above applies. Only sent when there is a rate for it to describe.
   *
   * Defaulted rather than required, because "unstated" already has a meaning: exclusive is how every
   * price authored before this existed is charged. A caller that has never heard of tax modes — an
   * older client, a script, a test fixture — therefore keeps describing exactly the price it meant.
   */
  taxMode: z.enum(TAX_MODES).default("Exclusive"),
});

export const createSubscriptionPriceSchema = subscriptionPriceFieldsSchema;

export type CreateSubscriptionPriceFormValues = z.infer<typeof createSubscriptionPriceSchema>;

export const defaultSubscriptionPriceFormValues: CreateSubscriptionPriceFormValues = {
  currencyCode: "USD",
  amount: 0,
  interval: 2,
  intervalCount: 1,
  displayPriceNote: "",
  quantityItemKey: FLAT_FEE,
  taxPercent: undefined,
  // Exclusive by default because it is the reading that matches every price authored before this
  // existed, so an author who ignores this section changes nothing.
  taxMode: "Exclusive",
};
