import { z } from "zod";

import {
  isRepresentableInMinorUnits,
  minorUnitExponent,
} from "../utilities/subscription-format";

import { AUTOMATIC_DISCOUNT_COMBINATIONS } from "../utilities/subscription-discount";
import {
  BILLING_ALIGNMENT_NAMES,
  CALENDAR_ANNUAL_CHARGE_TIMING_NAMES,
} from "../models/subscription-plan.model";
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
const priceFields = z.object({
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
  /**
   * Where renewals land. Defaulted rather than required, because "unstated" already means
   * anniversary everywhere else — on the server, and on every price authored before this existed.
   *
   * Not validated against the cadence here. The field is only shown for a monthly price billed
   * once a month, and submit sends it only for that cadence, so an author cannot produce the
   * invalid combination through this form; the server refuses it regardless.
   */
  billingAlignment: z.enum(BILLING_ALIGNMENT_NAMES).default("Anniversary"),
  /**
   * The monthly price a calendar-aligned yearly price is charged from. Empty for every other
   * price; submit drops it rather than sending one the server would refuse.
   *
   * Not required here even when the cadence needs one: the field only appears for that cadence, and
   * a resolver error on a hidden control is a form that cannot be submitted and cannot say why.
   * The server refuses the combination regardless.
   */
  calendarStubBasePriceId: z.string().optional(),
  /**
   * When the annual amount is collected. Defaulted rather than required, because "unstated" already
   * means the conservative answer everywhere else: collect the year when the year starts.
   */
  calendarAnnualChargeTiming: z
    .enum(CALENDAR_ANNUAL_CHARGE_TIMING_NAMES)
    .default("AtBoundary"),
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
  /**
   * A percentage taken off this price without a code, converted to basis points on submit. Optional
   * and empty-string-tolerant for the same reasons the tax rate is: a cleared number input holds
   * `""`, and coercing that to zero would author a discount of nothing rather than no discount.
   */
  automaticDiscountPercent: z.preprocess(
    (value) => (value === "" || value === null || value === undefined ? undefined : value),
    z.coerce
      .number()
      .min(0, "A discount cannot be negative.")
      .max(100, "A discount cannot exceed 100%.")
      .optional(),
  ),
  /**
   * How that discount meets a volume band. Defaulted rather than required, because "unstated" has a
   * safe meaning: take the better of the two and never both, which cannot give away more than the
   * author wrote.
   */
  quantityDiscountCombination: z
    .enum(AUTOMATIC_DISCOUNT_COMBINATIONS)
    .default("BestDiscount"),
});

/**
 * The same fields, with the one rule that needs two of them at once.
 *
 * How many decimals an amount may carry is a property of the currency beside it, so it cannot be
 * expressed on the amount alone. Left unchecked, submit rounds: 89.999 CHF is charged as 90.00 and
 * 100.5 JPY as 101, with nothing said to the author either time.
 */
export const subscriptionPriceFieldsSchema = priceFields.superRefine((price, context) => {
  if (isRepresentableInMinorUnits(price.amount, price.currencyCode)) {
    return;
  }

  const exponent = minorUnitExponent(price.currencyCode);

  context.addIssue({
    code: z.ZodIssueCode.custom,
    path: ["amount"],
    message:
      exponent === 0
        ? `${price.currencyCode} has no decimal places — enter a whole amount.`
        : `${price.currencyCode} allows at most ${exponent} decimal places.`,
  });
});

export const createSubscriptionPriceSchema = subscriptionPriceFieldsSchema;

export type CreateSubscriptionPriceFormValues = z.infer<typeof createSubscriptionPriceSchema>;

export const defaultSubscriptionPriceFormValues: CreateSubscriptionPriceFormValues = {
  currencyCode: "USD",
  amount: 0,
  interval: 2,
  intervalCount: 1,
  // Anniversary by default, so an author who never opens this section sells the price they always
  // would have.
  billingAlignment: "Anniversary",
  calendarStubBasePriceId: undefined,
  calendarAnnualChargeTiming: "AtBoundary",
  displayPriceNote: "",
  quantityItemKey: FLAT_FEE,
  taxPercent: undefined,
  automaticDiscountPercent: undefined,
  quantityDiscountCombination: "BestDiscount",
  // Exclusive by default because it is the reading that matches every price authored before this
  // existed, so an author who ignores this section changes nothing.
  taxMode: "Exclusive",
};
