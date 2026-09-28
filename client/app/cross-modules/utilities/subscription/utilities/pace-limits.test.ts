import { describe, expect, it } from "vitest";
import { paceWarnings, type PaceLimit } from "./pace-limits";

const limit = (overrides: Partial<PaceLimit> = {}): PaceLimit => ({
  window: 0,
  count: 1,
  rolling: true,
  quantity: 5,
  behaviour: 0,
  ...overrides,
});

const MONTHLY = { usageInterval: 2, usageIntervalCount: 1, resetPolicy: 0 };

const messagesFor = (warnings: ReturnType<typeof paceWarnings>, index: number) =>
  warnings.filter((warning) => warning.index === index).map((warning) => warning.message);

describe("a limit the included amount already holds usage under", () => {
  /**
   * 100 a month with overage off: a rolling week can straddle a month end and see at most the
   * last 100 of one month and the first 100 of the next, so 200 a week can never refuse anything
   * the budget has not refused first.
   */
  it("warns on a weekly limit the monthly budget can never let through", () => {
    const warnings = paceWarnings([limit(), limit({ window: 2, quantity: 200 })], {
      ...MONTHLY,
      includedQuantity: 100,
      overageAllowed: false,
    });

    expect(messagesFor(warnings, 1).join()).toMatch(/stops at 100 per allowance period/);
    expect(messagesFor(warnings, 0)).toEqual([]);
  });

  it("says nothing while the limit is below what the budget could let through", () => {
    const warnings = paceWarnings([limit({ window: 2, quantity: 150 })], {
      ...MONTHLY,
      includedQuantity: 100,
      overageAllowed: false,
    });

    expect(messagesFor(warnings, 0)).toEqual([]);
  });

  /** With overage on, the limit is what caps how fast billed overage is run up. */
  it("says nothing when overage is on", () => {
    const warnings = paceWarnings([limit({ window: 2, quantity: 200 })], {
      ...MONTHLY,
      includedQuantity: 100,
      overageAllowed: true,
    });

    expect(messagesFor(warnings, 0)).toEqual([]);
  });

  /** A daily allowance of 100 lets up to 800 into any week, so 200 a week does real work. */
  it("counts every short period a limit's span can reach into", () => {
    const warnings = paceWarnings([limit({ window: 2, quantity: 200 })], {
      usageInterval: 0,
      usageIntervalCount: 1,
      resetPolicy: 0,
      includedQuantity: 100,
      overageAllowed: false,
    });

    expect(messagesFor(warnings, 0)).toEqual([]);
  });

  it("adds what a carry-forward meter may bring into a period", () => {
    const warnings = paceWarnings([limit({ window: 2, quantity: 250 })], {
      ...MONTHLY,
      resetPolicy: 2,
      includedQuantity: 100,
      carryForwardCap: 50,
      overageAllowed: false,
    });

    // 150 a period, two periods at most: 300, so 250 can still be reached.
    expect(messagesFor(warnings, 0)).toEqual([]);
  });

  it("compares a lifetime allowance against its whole amount", () => {
    const warnings = paceWarnings([limit({ window: 2, quantity: 100 })], {
      ...MONTHLY,
      resetPolicy: 1,
      includedQuantity: 100,
      overageAllowed: false,
    });

    expect(messagesFor(warnings, 0).join()).toMatch(/for its whole lifetime/);
  });
});

describe("a longer limit a shorter one already holds under", () => {
  /** "This stretch" left the author to work out which stretch, and against which limit. */
  it("names the limit doing the capping and the stretch it caps", () => {
    const warnings = paceWarnings(
      [limit({ count: 5, quantity: 10_000 }), limit({ window: 2, quantity: 500_000 })],
      { ...MONTHLY, includedQuantity: 10_000_000, overageAllowed: false },
    );

    expect(messagesFor(warnings, 1)).toContain(
      "The limit of 10,000 per 5 hours already caps a week at about 340,000, so this one never applies.",
    );
  });
});
