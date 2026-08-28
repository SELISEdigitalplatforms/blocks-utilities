import { describe, expect, it } from "vitest";
import { describeDiscountAmountProblem } from "./discount-amount";
import {
  exampleMinorAmount,
  isRepresentableInMinorUnits,
  minorUnitStep,
  toMajorUnits,
  toMinorUnits,
} from "./subscription-format";

/**
 * The exponent is the whole of this. A currency's minor unit is not always a hundredth: yen has
 * none, and dinars have a thousandth, so an authoring field that assumes two decimals is wrong in
 * both directions — it refuses a whole number of yen with a decimal point, and it silently rounds
 * away the third digit of a dinar amount.
 */
describe("major-unit money entry", () => {
  describe("what the author types reaches the API as", () => {
    it.each([
      ["145.00", "CHF", 14_500],
      ["0.05", "USD", 5],
      ["100", "JPY", 100],
      ["1.250", "KWD", 1_250],
      // Two decimals in a three-decimal currency is a real price, not a rounding: 1.25 dinars is
      // 1250 fils, and the trailing zero is not something the author has to type.
      ["1.25", "KWD", 1_250],
      ["0", "CHF", 0],
    ])("sends %s %s as %i", (typed, currency, expected) => {
      expect(toMinorUnits(Number(typed), currency)).toBe(expected);
    });
  });

  describe("what is stored reads back as what was typed", () => {
    it.each([
      [14_500, "CHF", 145],
      [5, "USD", 0.05],
      [100, "JPY", 100],
      [1_250, "KWD", 1.25],
    ])("reopens %i %s as %d", (stored, currency, expected) => {
      expect(toMajorUnits(stored, currency)).toBe(expected);
    });

    it("round-trips without moving the stored figure", () => {
      // The property that matters for editing: opening a plan and saving it unchanged must not
      // reprice anything.
      for (const [stored, currency] of [
        [14_500, "CHF"],
        [5, "USD"],
        [100, "JPY"],
        [1_250, "KWD"],
        [1, "BHD"],
        [999, "JPY"],
      ] as const) {
        expect(toMinorUnits(toMajorUnits(stored, currency), currency)).toBe(stored);
      }
    });
  });

  describe("precision the currency cannot express", () => {
    it.each([
      [0.05, "CHF"],
      [145, "CHF"],
      [0.01, "USD"],
      [100, "JPY"],
      [1.25, "KWD"],
      [1.001, "BHD"],
      // Held as 0.07000000000000001 once it has been through Number(), and still seven centimes.
      [0.07, "CHF"],
      [8.29, "EUR"],
    ])("accepts %d %s", (amount, currency) => {
      expect(isRepresentableInMinorUnits(amount, currency)).toBe(true);
    });

    it.each([
      // Half a centime. Rounds to 1 and charges double what was written.
      [0.005, "CHF"],
      [89.999, "CHF"],
      // Yen has no decimals at all, so any fraction is a price it cannot carry.
      [100.5, "JPY"],
      [0.5, "JPY"],
      // A fourth digit in a three-decimal currency.
      [1.2345, "KWD"],
      [Number.NaN, "CHF"],
      [Number.POSITIVE_INFINITY, "USD"],
    ])("refuses %d %s", (amount, currency) => {
      expect(isRepresentableInMinorUnits(amount, currency)).toBe(false);
    });
  });

  describe("what the control offers", () => {
    it.each([
      ["CHF", "0.01"],
      ["USD", "0.01"],
      ["EUR", "0.01"],
      ["JPY", "1"],
      ["KWD", "0.001"],
      ["BHD", "0.001"],
    ])("steps %s by %s", (currency, step) => {
      expect(minorUnitStep(currency)).toBe(step);
    });

    it("never suggests an example the same field would reject", () => {
      for (const currency of ["CHF", "USD", "JPY", "KWD", "BHD", "GBP"]) {
        const example = exampleMinorAmount(currency);

        expect(isRepresentableInMinorUnits(Number(example), currency)).toBe(true);
      }
    });

    it("suggests the currency's smallest coin rather than a fixed 0.05", () => {
      expect(exampleMinorAmount("CHF")).toBe("0.05");
      // Not "0.05" — there is no such amount in yen, and a placeholder showing one would be
      // demonstrating the mistake this change exists to remove.
      expect(exampleMinorAmount("JPY")).toBe("5");
      expect(exampleMinorAmount("KWD")).toBe("0.005");
    });
  });
});

describe("describeDiscountAmountProblem", () => {
  it.each([
    ["145.00", "CHF"],
    ["0.05", "USD"],
    ["100", "JPY"],
    ["1.250", "KWD"],
  ])("passes %s %s", (amount, currency) => {
    expect(describeDiscountAmountProblem(amount, currency)).toBeNull();
  });

  it("asks for an amount rather than sending nothing", () => {
    expect(describeDiscountAmountProblem("", "CHF")).toBe("Enter an amount to take off.");
    expect(describeDiscountAmountProblem("   ", "CHF")).toBe("Enter an amount to take off.");
  });

  it("refuses a discount that takes nothing off", () => {
    // Unlike a tier price, where zero is a real decision about overage.
    expect(describeDiscountAmountProblem("0", "CHF")).toBe(
      "An amount off has to be more than zero.",
    );
    expect(describeDiscountAmountProblem("-5", "CHF")).toBe(
      "An amount off has to be more than zero.",
    );
    expect(describeDiscountAmountProblem("abc", "CHF")).toBe(
      "An amount off has to be more than zero.",
    );
  });

  it("names the currency's own limit rather than rounding to it", () => {
    expect(describeDiscountAmountProblem("0.005", "CHF")).toBe(
      "CHF allows at most 2 decimal places.",
    );
    expect(describeDiscountAmountProblem("1.2345", "KWD")).toBe(
      "KWD allows at most 3 decimal places.",
    );
    expect(describeDiscountAmountProblem("100.5", "JPY")).toBe(
      "JPY has no decimal places — enter a whole amount.",
    );
  });
});
