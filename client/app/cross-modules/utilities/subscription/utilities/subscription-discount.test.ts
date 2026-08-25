import { describe, expect, it } from "vitest";

import { describeAutomaticDiscount, discountBreakdown } from "./subscription-discount";

/**
 * Currency formatting puts a non-breaking space between the code and the number, which no test
 * should have to spell. Normalised here so the assertions read the way the sentence does.
 */
const plain = (sentence: string | null) => sentence?.replace(/\u00a0/g, " ") ?? null;

/**
 * The preview arithmetic for automatic discounts, which has to agree with the server's to the cent.
 *
 * The figures below are the same ones asserted in `AutomaticPriceDiscountTests` on the server: 8%
 * for the cadence and 5% for the volume against a gross of 100,000, because that is the pair the two
 * combinations disagree about — 13% one way, 8% the other.
 */
describe("discount breakdown", () => {
  it("adds the two rates under Additive and applies them once", () => {
    expect(
      discountBreakdown({
        grossMinor: 100_000,
        automaticBasisPoints: 800,
        quantityBasisPoints: 500,
        combination: "Additive",
      }),
    ).toEqual({ discountMinor: 13_000, effectiveBasisPoints: 1_300, subtotalMinor: 87_000 });
  });

  it("takes only the larger rate under BestDiscount", () => {
    expect(
      discountBreakdown({
        grossMinor: 100_000,
        automaticBasisPoints: 800,
        quantityBasisPoints: 500,
        combination: "BestDiscount",
      }),
    ).toEqual({ discountMinor: 8_000, effectiveBasisPoints: 800, subtotalMinor: 92_000 });
  });

  it("caps an additive pair at everything", () => {
    // A charge must never arrive negative, whatever two rates an author writes.
    expect(
      discountBreakdown({
        grossMinor: 10_000,
        automaticBasisPoints: 6_000,
        quantityBasisPoints: 6_000,
        combination: "Additive",
      }),
    ).toEqual({ discountMinor: 10_000, effectiveBasisPoints: 10_000, subtotalMinor: 0 });
  });

  it("leaves a price with no automatic discount on its band alone", () => {
    // The state every stored price is in. Its arithmetic must not move.
    expect(
      discountBreakdown({
        grossMinor: 9_999,
        automaticBasisPoints: 0,
        quantityBasisPoints: 500,
        combination: "Additive",
      }),
    ).toEqual({ discountMinor: 499, effectiveBasisPoints: 500, subtotalMinor: 9_500 });
  });

  it("truncates a discount rather than rounding it up", () => {
    // The direction that favours the customer, and the direction the server's bands already take.
    // Rounding up would take more off than the percentage advertised.
    expect(
      discountBreakdown({
        grossMinor: 999,
        automaticBasisPoints: 800,
        quantityBasisPoints: 0,
        combination: "BestDiscount",
      }).discountMinor,
    ).toBe(79);
  });

  it("says nothing came off a charge of nothing", () => {
    expect(
      discountBreakdown({
        grossMinor: 0,
        automaticBasisPoints: 800,
        quantityBasisPoints: 500,
        combination: "Additive",
      }),
    ).toEqual({ discountMinor: 0, effectiveBasisPoints: 0, subtotalMinor: 0 });
  });
});

describe("automatic discount preview", () => {
  it("prices the default quantity through its band and then taxes the remainder", () => {
    const sentence = plain(describeAutomaticDiscount({
      amount: 100,
      currencyCode: "CHF",
      quantity: 10,
      automaticDiscountPercent: 8,
      quantityDiscountPercent: 5,
      combination: "Additive",
      taxPercent: 7.7,
      taxMode: "Exclusive",
    }));

    // 10 × CHF 100 is CHF 1,000; 13% off is CHF 130; 7.7% of the CHF 870 left is CHF 66.99.
    expect(sentence).toContain("CHF 130.00");
    expect(sentence).toContain("13%");
    expect(sentence).toContain("CHF 936.99");
  });

  it("describes an inclusive price as already containing its tax", () => {
    const sentence = plain(describeAutomaticDiscount({
      amount: 100,
      currencyCode: "CHF",
      quantity: 1,
      automaticDiscountPercent: 8,
      quantityDiscountPercent: 0,
      combination: "BestDiscount",
      taxPercent: 7.7,
      taxMode: "Inclusive",
    }));

    // The customer pays CHF 92.00 — the discounted amount itself — with the tax found inside it,
    // rather than 92.00 plus tax on top of it.
    expect(sentence).toContain("= CHF 92.00 including CHF 6.58 tax");
    expect(sentence).not.toContain("+");
  });

  it("says nothing when there is no discount to explain", () => {
    expect(
      describeAutomaticDiscount({
        amount: 100,
        currencyCode: "CHF",
        quantity: 1,
        automaticDiscountPercent: undefined,
        quantityDiscountPercent: 0,
        combination: "BestDiscount",
        taxPercent: 7.7,
        taxMode: "Exclusive",
      }),
    ).toBeNull();
  });

  it("says nothing before an amount has been typed", () => {
    expect(
      describeAutomaticDiscount({
        amount: undefined,
        currencyCode: "CHF",
        quantity: 1,
        automaticDiscountPercent: 8,
        quantityDiscountPercent: 0,
        combination: "BestDiscount",
        taxPercent: undefined,
        taxMode: "Exclusive",
      }),
    ).toBeNull();
  });

  it("omits the tax clause on an untaxed price", () => {
    const sentence = plain(describeAutomaticDiscount({
      amount: 100,
      currencyCode: "CHF",
      quantity: 1,
      automaticDiscountPercent: 8,
      quantityDiscountPercent: 0,
      combination: "BestDiscount",
      taxPercent: undefined,
      taxMode: "Exclusive",
    }));

    expect(sentence).toBe("CHF 100.00 − CHF 8.00 (8%) = CHF 92.00");
  });
});
