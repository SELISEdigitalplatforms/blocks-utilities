import { describe, expect, it } from "vitest";

import {
  formatQuantity,
  isWithinMagnitude,
  isWithinScale,
  METER_QUANTITY_MAX_MAGNITUDE,
  METER_QUANTITY_MAX_SCALE,
  scaleOf,
  stepFor,
} from "./meter-quantity";

describe("scaleOf", () => {
  it.each([
    [0, 0],
    [1, 0],
    [-7, 0],
    [0.5, 1],
    [512.5, 1],
    [0.001, 3],
    [-0.25, 2],
  ])("reads %s as %s decimal places", (value, expected) => {
    expect(scaleOf(value)).toBe(expected);
  });

  /**
   * JavaScript renders anything below 1e-6 in exponent form, and 1e-6 is the first quantity a
   * six-place meter can hold — so the boundary of what is allowed is exactly where the string
   * representation changes shape.
   */
  it("reads exponent form as the places it stands for", () => {
    expect(scaleOf(1e-7)).toBe(7);
    expect(scaleOf(1.5e-7)).toBe(8);
    expect(scaleOf(0.000001)).toBe(6);
  });

  it("does not count trailing zeroes, which a number never carries anyway", () => {
    expect(scaleOf(1.5)).toBe(1);
    expect(scaleOf(500.0)).toBe(0);
  });

  it("treats a large integer in exponent form as whole", () => {
    expect(scaleOf(1e21)).toBe(0);
  });
});

describe("isWithinScale", () => {
  /** The state of every meter authored before fractions existed. */
  it.each([
    [100, true],
    [0, true],
    [0.5, false],
    [100.1, false],
  ])("at scale zero, %s is %s", (value, expected) => {
    expect(isWithinScale(value, 0)).toBe(expected);
  });

  it.each([
    [512.5, 3, true],
    [512.001, 3, true],
    [512.0001, 3, false],
    [512, 3, true],
  ])("at a declared scale, %s with %s places is %s", (value, scale, expected) => {
    expect(isWithinScale(value, scale)).toBe(expected);
  });

  /**
   * The check that a naive implementation gets wrong: scaling by a power of ten to look for a
   * remainder makes 1.15 * 100 come out as 114.99999999999999.
   */
  it("does not report a remainder for a value binary arithmetic cannot hold", () => {
    expect(isWithinScale(1.15, 2)).toBe(true);
    expect(isWithinScale(8.165, 3)).toBe(true);
  });

  it("rejects anything when the scale itself is out of range", () => {
    expect(isWithinScale(0.5, METER_QUANTITY_MAX_SCALE)).toBe(true);
    expect(isWithinScale(0.5, -1)).toBe(false);
  });
});

describe("isWithinMagnitude", () => {
  it("bounds the range in both directions", () => {
    expect(isWithinMagnitude(METER_QUANTITY_MAX_MAGNITUDE)).toBe(true);
    expect(isWithinMagnitude(-METER_QUANTITY_MAX_MAGNITUDE)).toBe(true);
    expect(isWithinMagnitude(METER_QUANTITY_MAX_MAGNITUDE + 1)).toBe(false);
  });

  it("refuses what is not a number at all", () => {
    expect(isWithinMagnitude(Number.NaN)).toBe(false);
    expect(isWithinMagnitude(Number.POSITIVE_INFINITY)).toBe(false);
  });
});

describe("stepFor", () => {
  /**
   * A whole-unit meter keeps the step the input already had, so nothing about an existing form
   * changes. Without a matching step the browser's own number validation refuses a fraction
   * before the form's validation is ever consulted.
   */
  it.each([
    [0, "1"],
    [1, "0.1"],
    [3, "0.001"],
    [6, "0.000001"],
  ])("steps a scale of %s by %s", (scale, expected) => {
    expect(stepFor(scale)).toBe(expected);
  });
});

describe("formatQuantity", () => {
  it("does not show places the author never typed", () => {
    expect(formatQuantity(500)).toBe("500");
    expect(formatQuantity(512.5)).toBe("512.5");
  });
});
