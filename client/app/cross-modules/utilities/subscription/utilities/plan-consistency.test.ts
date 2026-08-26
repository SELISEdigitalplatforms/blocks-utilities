import { describe, expect, it } from "vitest";
import { describeEntitlementMeterMismatch } from "./plan-consistency";

const meter = (overrides: Partial<Parameters<typeof describeEntitlementMeterMismatch>[1][number]> = {}) => ({
  meterKey: "ses-signatures",
  unitLabel: "signature",
  includedQuantity: 150,
  overageAllowed: true,
  ...overrides,
});

const counted = (limit: number) => ({
  limitKind: "Count",
  limit,
  meterKey: "ses-signatures",
});

describe("describeEntitlementMeterMismatch", () => {
  it("says nothing when the limit matches the meter's allowance", () => {
    expect(describeEntitlementMeterMismatch(counted(150), [meter()])).toBeNull();
  });

  it("warns that the excess is billed while still reported as allowed", () => {
    const message = describeEntitlementMeterMismatch(counted(500), [meter()]);

    expect(message).toContain("500 signatures");
    expect(message).toContain("150");
    expect(message).toContain("350 signatures are billed as overage");
  });

  it("warns that the excess is unreachable when the meter blocks instead of billing", () => {
    const message = describeEntitlementMeterMismatch(counted(500), [
      meter({ overageAllowed: false }),
    ]);

    expect(message).toContain("blocks at 150");
    expect(message).toContain("can never be used");
  });

  it("warns when the entitlement permits less than the meter includes", () => {
    const message = describeEntitlementMeterMismatch(counted(100), [meter()]);

    expect(message).toContain("Permits only 100 signatures");
    expect(message).toContain("50 signatures of the allowance can never be used");
  });

  it("says nothing for entitlements that carry no number", () => {
    expect(
      describeEntitlementMeterMismatch(
        { limitKind: "Unlimited", limit: null, meterKey: "ses-signatures" },
        [meter()],
      ),
    ).toBeNull();
    expect(
      describeEntitlementMeterMismatch(
        { limitKind: "Boolean", limit: null, meterKey: null },
        [meter()],
      ),
    ).toBeNull();
  });

  it("says nothing while the meter is still being chosen", () => {
    expect(describeEntitlementMeterMismatch(counted(500), [])).toBeNull();
  });

  it("uses a singular unit for a difference of one", () => {
    expect(describeEntitlementMeterMismatch(counted(151), [meter()])).toContain(
      "1 signature is billed",
    );
  });
});
