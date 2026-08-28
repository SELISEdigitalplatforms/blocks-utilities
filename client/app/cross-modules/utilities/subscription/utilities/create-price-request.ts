import type { CreateSubscriptionPriceRequest } from "../models/subscription-plan.model";
import type { CreateSubscriptionPriceFormValues } from "../schemas/subscription-price.schema";
import { FLAT_FEE } from "../schemas/subscription-price.schema";
import { isCalendarEligible, requiresStubBasePrice } from "./billing-alignment";
import { toMinorUnits } from "./subscription-format";
import { toBasisPoints } from "./subscription-tax";

/**
 * One authored price, in the shape the API wants.
 *
 * Extracted so the two places that add a price — finishing the plan builder, and adding a price to
 * a plan that is already selling — cannot describe the same price differently. They did: the page
 * this replaced hand-rolled its own mapping and simply had no fields for billing alignment, tax or
 * automatic discount, so a price added there was quietly less than a price added in the builder.
 *
 * The conditional fields are the point. Each one is a combination the server refuses outright, and
 * the form can drift into every one of them: an author picks the calendar alignment and then
 * changes the cadence, or clears a tax rate and leaves the mode behind.
 */
export const toCreatePriceRequest = ({
  price,
  planId,
  organizationId,
}: {
  price: CreateSubscriptionPriceFormValues;
  planId: string;
  /**
   * The plan may belong to an organization the console is not itself in, and the server resolves
   * each request on its own — without naming it, the plan reads as missing.
   */
  organizationId?: string;
}): CreateSubscriptionPriceRequest => ({
  planId,
  organizationId,
  currencyCode: price.currencyCode,
  unitAmountMinor: toMinorUnits(price.amount, price.currencyCode),
  interval: price.interval,
  intervalCount: price.intervalCount,
  // Only for the cadence that can carry it. Sending "CalendarMonth" alongside a quarterly cadence
  // is the one combination the server refuses outright.
  billingAlignment: isCalendarEligible(price) ? price.billingAlignment : undefined,
  // Only for the one cadence that carries it. A link left behind by an author who chose
  // yearly-calendar and then changed the cadence is refused outright by the server.
  calendarStubBasePriceId: requiresStubBasePrice(price)
    ? price.calendarStubBasePriceId
    : undefined,
  // Same cadence rule as the link it belongs to.
  calendarAnnualChargeTiming: requiresStubBasePrice(price)
    ? price.calendarAnnualChargeTiming
    : undefined,
  displayPriceNote: price.displayPriceNote?.trim() || undefined,
  quantityItemKey: price.quantityItemKey === FLAT_FEE ? undefined : price.quantityItemKey,
  // Both or neither. The server refuses a rate without a mode — deliberately, since the same
  // number means two different prices — and a mode without a rate would describe a tax that does
  // not apply.
  taxRateBasisPoints: price.taxPercent ? toBasisPoints(price.taxPercent) : undefined,
  taxMode: price.taxPercent ? price.taxMode : undefined,
  // Both or neither again: a combination without a discount would describe how a reduction that
  // does not exist meets a band.
  automaticDiscountBasisPoints: price.automaticDiscountPercent
    ? toBasisPoints(price.automaticDiscountPercent)
    : undefined,
  quantityDiscountCombination: price.automaticDiscountPercent
    ? price.quantityDiscountCombination
    : undefined,
});

/**
 * Adds each price in turn, collecting the ones that did not land.
 *
 * Sequential rather than concurrent, and tolerant of individual failures, for the same reason the
 * plan builder is: whatever landed stays landed, so a failure has to be reported as the partial
 * result it is rather than as a submission to retry wholesale.
 */
export const createPricesInTurn = async ({
  prices,
  planId,
  organizationId,
  createPrice,
}: {
  prices: CreateSubscriptionPriceFormValues[];
  planId: string;
  organizationId?: string;
  createPrice: (request: CreateSubscriptionPriceRequest) => Promise<unknown>;
}): Promise<string[]> => {
  const failures: string[] = [];

  for (const [index, price] of prices.entries()) {
    try {
      await createPrice(toCreatePriceRequest({ price, planId, organizationId }));
    } catch (error) {
      failures.push(
        `Price ${index + 1} (${price.currencyCode} ${price.amount}): ${
          error instanceof Error ? error.message : "could not be added"
        }`,
      );
    }
  }

  return failures;
};
