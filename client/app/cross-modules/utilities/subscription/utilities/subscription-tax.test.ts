import { describe, expect, it } from "vitest";

import { describeTax, fromBasisPoints, taxBreakdown, toBasisPoints } from "./subscription-tax";

/**
 * The preview arithmetic, which has to agree with the server's to the cent.
 *
 * It never decides what anybody is charged — the server does that — but a preview that disagreed
 * with the charge would be worse than no preview at all, because an author would trust it. The
 * figures below are the same ones asserted in `SubscriptionTaxModeTests` on the server.
 */
describe("tax basis points", () => {
  it("rounds a percentage to basis points rather than truncating it", () => {
    // 7.7 is 7.699999… in binary floating point, so truncating authors 7.69% for everybody who
    // typed 7.7 — a rate that is wrong by a hundredth on every invoice forever.
    expect(toBasisPoints(7.7)).toBe(770);
    expect(toBasisPoints(20)).toBe(2000);
    expect(toBasisPoints(7.75)).toBe(775);
    expect(fromBasisPoints(770)).toBe(7.7);
  });
});

describe("tax breakdown", () => {
  it("adds tax above an exclusive amount", () => {
    expect(taxBreakdown({ amountMinor: 14_500, basisPoints: 770, mode: "Exclusive" })).toEqual({
      netMinor: 14_500,
      taxMinor: 1_117,
      totalMinor: 15_617,
    });
  });

  it("finds tax inside an inclusive amount", () => {
    expect(taxBreakdown({ amountMinor: 14_500, basisPoints: 770, mode: "Inclusive" })).toEqual({
      netMinor: 13_463,
      taxMinor: 1_037,
      totalMinor: 14_500,
    });
  });

  it("always splits an inclusive amount back into itself", () => {
    for (const amountMinor of [1, 99, 100, 14_500, 999_999]) {
      const { netMinor, taxMinor, totalMinor } = taxBreakdown({
        amountMinor,
        basisPoints: 770,
        mode: "Inclusive",
      });

      expect(netMinor + taxMinor).toBe(totalMinor);
    }
  });

  it("rounds a half up, the way the server does", () => {
    // 7.7% of 14,500 is 1,116.5. Rounding one way here and the other way there would show a preview
    // a cent from the charge — the kind of disagreement nobody can explain to a customer.
    expect(taxBreakdown({ amountMinor: 14_500, basisPoints: 770, mode: "Exclusive" }).taxMinor)
      .toBe(1_117);
  });

  it("treats a zero rate as untaxed in either mode", () => {
    for (const mode of ["Exclusive", "Inclusive"] as const) {
      expect(taxBreakdown({ amountMinor: 14_500, basisPoints: 0, mode })).toEqual({
        netMinor: 14_500,
        taxMinor: 0,
        totalMinor: 14_500,
      });
    }
  });
});

describe("describeTax", () => {
  it("spells out an exclusive price as a sum", () => {
    expect(
      describeTax({
        amount: 145,
        currencyCode: "CHF",
        taxPercent: 7.7,
        taxMode: "Exclusive",
      }),
    ).toContain("+");
  });

  it("names the total and the tax inside it for an inclusive price", () => {
    const sentence = describeTax({
      amount: 145,
      currencyCode: "CHF",
      taxPercent: 7.7,
      taxMode: "Inclusive",
    });

    expect(sentence).toContain("including");
    // The number the customer pays is the number the author typed. That is the whole claim
    // inclusive pricing makes, so the preview has to state it.
    expect(sentence).toMatch(/145\.00/);
  });

  it("says nothing when there is no rate or no amount yet", () => {
    expect(
      describeTax({ amount: 145, currencyCode: "CHF", taxPercent: undefined, taxMode: "Exclusive" }),
    ).toBeNull();
    expect(
      describeTax({ amount: undefined, currencyCode: "CHF", taxPercent: 7.7, taxMode: "Exclusive" }),
    ).toBeNull();
    // Not "CHF 0.00 + CHF 0.00 tax": a half-filled form should be quiet, not wrong.
    expect(
      describeTax({ amount: 0, currencyCode: "CHF", taxPercent: 7.7, taxMode: "Exclusive" }),
    ).toBeNull();
  });

  it("says nothing when a rate is too small to reach one minor unit", () => {
    // 0.01% of CHF 1.00 is a hundredth of a cent. A preview claiming "+ CHF 0.00 tax" reads as a
    // rounding bug rather than as arithmetic.
    expect(
      describeTax({ amount: 1, currencyCode: "CHF", taxPercent: 0.01, taxMode: "Exclusive" }),
    ).toBeNull();
  });
});
