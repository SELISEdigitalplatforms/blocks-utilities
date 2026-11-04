import {
  isRepresentableInMinorUnits,
  minorUnitExponent,
} from "./subscription-format";
import { tSubscription } from "./subscription-i18n";

/**
 * Why a fixed discount's amount cannot be sent, or null when it can.
 *
 * Pulled out of the page it is read on so it can be checked without rendering anything, and
 * because the rule is about money rather than about a form.
 *
 * A fixed discount used to be authored in minor units: the field was labelled "Minor units off",
 * so 5 meant five centimes, and an author taking five francs off a plan typed 5 and gave away a
 * hundredth of what they intended. Now that the field holds the currency's own units, the two ways
 * it can still be wrong are worth naming rather than rounding away.
 */
export const describeDiscountAmountProblem = (
  amount: string,
  currencyCode: string,
): string | null => {
  if (amount.trim() === "") {
    return tSubscription("DISCOUNT_AMOUNT.ENTER_AMOUNT");
  }

  const value = Number(amount);

  if (!Number.isFinite(value) || value <= 0) {
    // Zero is refused here although a zero tier price is allowed: a band priced at nothing is a
    // real decision about overage, while a discount that takes nothing off is a code that does
    // nothing when somebody redeems it.
    return tSubscription("DISCOUNT_AMOUNT.MORE_THAN_ZERO");
  }

  if (!isRepresentableInMinorUnits(value, currencyCode)) {
    const exponent = minorUnitExponent(currencyCode);

    return exponent === 0
      ? tSubscription("DISCOUNT_AMOUNT.NO_DECIMALS", { currency: currencyCode })
      : tSubscription("DISCOUNT_AMOUNT.MAX_DECIMALS", { currency: currencyCode, count: exponent });
  }

  return null;
};
