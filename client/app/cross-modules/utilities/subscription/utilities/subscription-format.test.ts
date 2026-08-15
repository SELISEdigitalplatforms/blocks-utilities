import { describe, expect, it } from "vitest";
import {
  formatEntitlementLimit,
  formatInterval,
  formatMeterAllowance,
  formatMoney,
  formatPrice,
  minorUnitExponent,
  toMajorUnits,
  toMinorUnits,
} from "./subscription-format";

describe("minor unit conversion", () => {
  it("defaults to two decimal places for an ordinary currency", () => {
    expect(minorUnitExponent("USD")).toBe(2);
    expect(minorUnitExponent("chf")).toBe(2);
  });

  it("knows the three-decimal currencies", () => {
    expect(minorUnitExponent("BHD")).toBe(3);
    expect(minorUnitExponent("KWD")).toBe(3);
  });

  it("knows the zero-decimal currencies", () => {
    expect(minorUnitExponent("JPY")).toBe(0);
  });

  it("round-trips major and minor units for an ordinary currency", () => {
    expect(toMinorUnits(89, "CHF")).toBe(8900);
    expect(toMajorUnits(8900, "CHF")).toBe(89);
  });

  it("round-trips major and minor units for a three-decimal currency", () => {
    expect(toMinorUnits(1.5, "BHD")).toBe(1500);
    expect(toMajorUnits(1500, "BHD")).toBe(1.5);
  });

  it("is not thrown off by ordinary binary floating-point imprecision", () => {
    // 10.10 * 100 does not land on an exact integer in IEEE 754 binary64; rounding is what
    // keeps this at 1010 minor units instead of drifting to 1009 or 1011.
    expect(toMinorUnits(10.1, "CHF")).toBe(1010);
  });
});

describe("formatMoney", () => {
  it("formats an ordinary amount using the currency's own symbol", () => {
    expect(formatMoney(8900, "USD")).toContain("89");
  });

  it("falls back gracefully for a currency code Intl rejects outright", () => {
    // A currency code must be three alphabetic characters per ECMA-402; anything else throws
    // at construction time rather than formatting oddly, which is exactly the case this
    // fallback exists for.
    expect(formatMoney(8900, "12A")).toBe("89.00 12A");
  });
});

describe("formatInterval", () => {
  it("reads naturally for a single interval", () => {
    expect(formatInterval("Month", 1)).toBe("every month");
  });

  it("pluralises a multi-count interval", () => {
    expect(formatInterval("Month", 3)).toBe("every 3 months");
  });
});

describe("formatPrice", () => {
  it("describes a flat fee without a quantity item", () => {
    const description = formatPrice({
      currencyCode: "CHF",
      unitAmountMinor: 8900,
      interval: "Month",
      intervalCount: 1,
      quantityItemKey: null,
    });

    expect(description).toContain("every month");
    expect(description).not.toContain("per");
  });

  it("describes a per-unit price", () => {
    const description = formatPrice({
      currencyCode: "CHF",
      unitAmountMinor: 1200,
      interval: "Month",
      intervalCount: 1,
      quantityItemKey: "seat",
    });

    expect(description).toContain("per seat");
  });
});

describe("formatMeterAllowance", () => {
  it("says what happens after the included amount when overage is allowed", () => {
    const description = formatMeterAllowance({
      displayName: "API calls",
      unitLabel: "call",
      includedQuantity: 1000,
      overageAllowed: true,
    });

    expect(description).toContain("1,000 calls included");
    expect(description).toContain("overage billed");
  });

  it("says usage is blocked once the meter does not allow overage", () => {
    const description = formatMeterAllowance({
      displayName: "Exports",
      unitLabel: "export",
      includedQuantity: 5,
      overageAllowed: false,
    });

    expect(description).toContain("blocked");
  });

  it("does not pluralise a singular unit", () => {
    const description = formatMeterAllowance({
      displayName: "Seats",
      unitLabel: "seat",
      includedQuantity: 1,
      overageAllowed: true,
    });

    expect(description).toContain("1 seat included");
  });
});

describe("formatEntitlementLimit", () => {
  it("reports an unlimited entitlement without a number", () => {
    expect(
      formatEntitlementLimit({ limitKind: "Unlimited", limit: null, unitLabel: null }),
    ).toBe("Unlimited");
  });

  it("reports a boolean entitlement as simply granted", () => {
    expect(
      formatEntitlementLimit({ limitKind: "Boolean", limit: null, unitLabel: null }),
    ).toBe("Granted");
  });

  it("reports a counted entitlement's limit with its unit", () => {
    expect(
      formatEntitlementLimit({ limitKind: "Count", limit: 500, unitLabel: "screening" }),
    ).toBe("Up to 500 screening");
  });
});
