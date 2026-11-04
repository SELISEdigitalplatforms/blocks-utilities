import { tSubscription } from "./subscription-i18n";

/**
 * How a volume band reads to a person.
 *
 * Shared rather than written at each surface: the builder's review step, the plan detail page and
 * the band editor all describe the same list, and three spellings of "30+ users, 20% off" is how a
 * catalogue comes to look like it says different things in different places.
 */
export const formatDiscountPercent = (discountBasisPoints: number): string => {
  if (discountBasisPoints <= 0) {
    return tSubscription("QUANTITY_DISCOUNT_FORMAT.NO_DISCOUNT");
  }

  // Basis points are integers, so at most two decimals can survive, and trailing zeros read as
  // false precision on a price list.
  const percent = discountBasisPoints / 100;

  return tSubscription("QUANTITY_DISCOUNT_FORMAT.PERCENT_OFF", {
    percent: Number(percent.toFixed(2)),
  });
};

export const formatQuantityBand = (
  tier: { minimumQuantity: number; maximumQuantity?: number | null },
  unitLabel: string,
): string => {
  const unit = unitLabel || tSubscription("QUANTITY_DISCOUNT_FORMAT.UNIT_FALLBACK");
  const range =
    tier.maximumQuantity === null || tier.maximumQuantity === undefined
      ? `${tier.minimumQuantity.toLocaleString()}+`
      : tier.minimumQuantity === tier.maximumQuantity
        ? tier.minimumQuantity.toLocaleString()
        : `${tier.minimumQuantity.toLocaleString()}\u2013${tier.maximumQuantity.toLocaleString()}`;

  return tSubscription("QUANTITY_DISCOUNT_FORMAT.BAND", {
    count: tier.maximumQuantity ?? 2,
    range,
    unit,
  });
};

export const describeQuantityBand = (
  tier: { minimumQuantity: number; maximumQuantity?: number | null; discountBasisPoints: number },
  unitLabel: string,
): string =>
  `${formatQuantityBand(tier, unitLabel)} \u2014 ${formatDiscountPercent(tier.discountBasisPoints)}`;
