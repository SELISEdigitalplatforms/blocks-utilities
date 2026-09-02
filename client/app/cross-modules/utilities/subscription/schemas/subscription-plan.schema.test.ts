import type { SafeParseReturnType } from "zod";
import { describe, expect, it } from "vitest";
import {
  buildSubscriptionPlanSchema,
  createSubscriptionPlanSchema,
  defaultSubscriptionPlanFormValues,
} from "./subscription-plan.schema";
import { FLAT_FEE } from "./subscription-price.schema";
import { TENANT_WIDE_ORGANIZATION } from "../constants/subscription.constants";

const price = {
  currencyCode: "EUR",
  amount: 3,
  interval: 2,
  intervalCount: 1,
  quantityItemKey: FLAT_FEE,
};

const validPlan = {
  ...defaultSubscriptionPlanFormValues,
  code: "pro",
  displayName: "Pro",
  organizationId: TENANT_WIDE_ORGANIZATION,
};

const issuePaths = (result: SafeParseReturnType<unknown, unknown>) =>
  result.success ? [] : result.error.issues.map((issue) => issue.path);

describe("createSubscriptionPlanSchema", () => {
  it("accepts a minimal tenant-wide plan", () => {
    const result = createSubscriptionPlanSchema.safeParse(validPlan);

    expect(result.success).toBe(true);
  });

  it("accepts a plan with no trial", () => {
    const result = createSubscriptionPlanSchema.safeParse(validPlan);

    expect(result.success).toBe(true);
  });

  it("accepts a days trial with a valid count", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      trialDurationKind: "Days",
      trialDurationCount: 14,
    });

    expect(result.success).toBe(true);
  });

  it("rejects a days trial with no count", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      trialDurationKind: "Days",
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["trialDurationCount"]);
  });

  it("rejects a days trial count outside 1-365", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      trialDurationKind: "Days",
      trialDurationCount: 366,
    });

    expect(result.success).toBe(false);
  });

  it("accepts an anniversary-months trial with a valid count", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      trialDurationKind: "AnniversaryMonths",
      trialDurationCount: 1,
    });

    expect(result.success).toBe(true);
  });

  it("rejects an anniversary-months trial count outside 1-12", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      trialDurationKind: "AnniversaryMonths",
      trialDurationCount: 13,
    });

    expect(result.success).toBe(false);
  });

  it("accepts an end-of-calendar-month trial with no count", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      trialDurationKind: "EndOfCalendarMonth",
    });

    expect(result.success).toBe(true);
  });

  it("rejects an end-of-calendar-month trial that specifies a count", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      trialDurationKind: "EndOfCalendarMonth",
      trialDurationCount: 1,
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["trialDurationCount"]);
  });

  it("rejects a plan code with characters the server would reject", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      code: "Pro Plan!",
    });

    expect(result.success).toBe(false);
  });

  it("requires an organization to be explicitly chosen", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      organizationId: "",
    });

    expect(result.success).toBe(false);
  });

  it("rejects malformed features JSON", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      featuresJson: "{not json",
    });

    expect(result.success).toBe(false);
  });

  it("accepts a features JSON object", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      featuresJson: '{"betaAccess": true}',
    });

    expect(result.success).toBe(true);
  });

  it("requires a counted entitlement to name both a limit and a meter", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [
        {
          meterKey: "api-calls",
          displayName: "API calls",
          unitLabel: "call",
          aggregation: 0,
          includedQuantity: 1000,
          overageAllowed: true,
          thresholdPercents: [],
          rateTables: [],
        },
      ],
      entitlements: [
        {
          key: "usage",
          limitKind: 1,
        },
      ],
    });

    expect(result.success).toBe(false);
  });

  it("accepts a counted entitlement that names an existing meter and limit", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [
        {
          meterKey: "api-calls",
          displayName: "API calls",
          unitLabel: "call",
          aggregation: 0,
          includedQuantity: 1000,
          overageAllowed: true,
          thresholdPercents: [],
          rateTables: [],
        },
      ],
      entitlements: [
        {
          key: "usage",
          limitKind: 1,
          limit: 1000,
          meterKey: "api-calls",
        },
      ],
    });

    expect(result.success).toBe(true);
  });

  it("rejects an entitlement naming a meter the plan does not define", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      entitlements: [
        {
          key: "usage",
          limitKind: 0,
          meterKey: "not-a-meter",
        },
      ],
    });

    expect(result.success).toBe(false);
  });

  it("rejects a trial grant naming a meter the plan does not define", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      trialDurationKind: "Days",
      trialDurationCount: 14,
      trialGrants: [{ meterKey: "not-a-meter", includedQuantity: 10 }],
    });

    expect(result.success).toBe(false);
  });

  it("rejects a quantity item whose maximum sits below its minimum", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      quantityItems: [
        {
          itemKey: "seat",
          unitLabel: "seat",
          minQuantity: 5,
          maxQuantity: 1,
          defaultQuantity: 5,
        },
      ],
    });

    expect(result.success).toBe(false);
  });

  it("keeps enum fields as numbers rather than strings, matching the request body", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [
        {
          meterKey: "api-calls",
          displayName: "API calls",
          unitLabel: "call",
          aggregation: 0,
          includedQuantity: 1000,
          overageAllowed: true,
          thresholdPercents: [80, 100],
          rateTables: [],
        },
      ],
    });

    expect(result.success).toBe(true);
    if (result.success) {
      expect(typeof result.data.meters[0].aggregation).toBe("number");
    }
  });

  it("accepts lifetime capacity when overage is blocked", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [
        {
          meterKey: "storage",
          displayName: "Storage",
          unitLabel: "byte",
          aggregation: 0,
          resetPolicy: 1,
          includedQuantity: 5_368_709_120,
          overageAllowed: false,
          thresholdPercents: [80, 100],
          rateTables: [],
        },
      ],
    });

    expect(result.success).toBe(true);
  });

  it("rejects monthly overage pricing on lifetime capacity", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [
        {
          meterKey: "storage",
          displayName: "Storage",
          unitLabel: "byte",
          aggregation: 0,
          resetPolicy: 1,
          includedQuantity: 5_368_709_120,
          overageAllowed: true,
          thresholdPercents: [],
          rateTables: [],
        },
      ],
    });

    expect(result.success).toBe(false);
  });

  const carryForwardMeter = (overrides: Record<string, unknown> = {}) => ({
    meterKey: "tokens",
    displayName: "Tokens",
    unitLabel: "token",
    aggregation: 0,
    resetPolicy: 2,
    includedQuantity: 1_000_000,
    overageAllowed: false,
    thresholdPercents: [],
    rateTables: [],
    ...overrides,
  });

  it("accepts a carry-forward meter that caps what rolls in", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [carryForwardMeter({ carryForwardCap: 500_000 })],
    });

    expect(result.success).toBe(true);
  });

  it("refuses a carry-forward meter with no cap", () => {
    // Without a ceiling a dormant subscription banks allowance indefinitely, which is never
    // what was sold.
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [carryForwardMeter()],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["meters", 0, "carryForwardCap"]);
  });

  it("refuses a cap on a meter that does not carry forward", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [carryForwardMeter({ resetPolicy: 0, carryForwardCap: 500_000 })],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["meters", 0, "carryForwardCap"]);
  });

  it("refuses a plan with no price, which nobody could subscribe to", () => {
    const result = createSubscriptionPlanSchema.safeParse({ ...validPlan, prices: [] });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["prices"]);
  });

  it("allows a plan with no price when editing, where prices are added separately", () => {
    const result = buildSubscriptionPlanSchema({ requirePrice: false }).safeParse({
      ...validPlan,
      prices: [],
    });

    expect(result.success).toBe(true);
  });

  it("rejects a price charging for a quantity item the plan does not define", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      prices: [{ ...price, quantityItemKey: "seat" }],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["prices", 0, "quantityItemKey"]);
  });

  it("accepts a price charging for a quantity item the plan does define", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      quantityItems: [
        {
          itemKey: "seat",
          unitLabel: "seat",
          minQuantity: 1,
          defaultQuantity: 1,
        },
      ],
      prices: [{ ...price, quantityItemKey: "seat" }],
    });

    expect(result.success).toBe(true);
  });

  it("rejects two prices with identical terms, which the server would reject after creating the plan", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      prices: [price, { ...price, amount: 99 }],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["prices", 1, "currencyCode"]);
  });

  it("accepts a monthly and an annual price on the same plan", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      prices: [price, { ...price, interval: 3, amount: 30 }],
    });

    expect(result.success).toBe(true);
  });
});

describe("quantity discount bands", () => {
  const withTiers = (
    tiers: { minimumQuantity: number; maximumQuantity?: number; discountPercent: number }[],
    item: { minQuantity?: number; maxQuantity?: number } = {},
  ) => ({
    ...validPlan,
    quantityItems: [
      {
        itemKey: "user",
        unitLabel: "user",
        minQuantity: item.minQuantity ?? 1,
        maxQuantity: item.maxQuantity,
        defaultQuantity: 1,
        quantityDiscountTiers: tiers,
      },
    ],
    prices: [{ ...price, quantityItemKey: "user" }],
  });

  const contiguous = [
    { minimumQuantity: 1, maximumQuantity: 4, discountPercent: 0 },
    { minimumQuantity: 5, maximumQuantity: 9, discountPercent: 5 },
    { minimumQuantity: 10, discountPercent: 10 },
  ];

  it("accepts a quantity item with no bands at all", () => {
    const result = createSubscriptionPlanSchema.safeParse(withTiers([]));

    expect(result.success).toBe(true);
  });

  it("accepts contiguous bands ending open on an unbounded item", () => {
    const result = createSubscriptionPlanSchema.safeParse(withTiers(contiguous));

    expect(result.success).toBe(true);
  });

  it("rejects a single band, which the unit price already expresses", () => {
    const result = createSubscriptionPlanSchema.safeParse(
      withTiers([{ minimumQuantity: 1, discountPercent: 10 }]),
    );

    expect(result.success).toBe(false);
  });

  it("rejects a first band that does not start where the item does", () => {
    const result = createSubscriptionPlanSchema.safeParse(
      withTiers([
        { minimumQuantity: 2, maximumQuantity: 4, discountPercent: 0 },
        { minimumQuantity: 5, discountPercent: 5 },
      ]),
    );

    expect(issuePaths(result)).toContainEqual([
      "quantityItems",
      0,
      "quantityDiscountTiers",
      0,
      "minimumQuantity",
    ]);
  });

  it("rejects a gap between bands", () => {
    const result = createSubscriptionPlanSchema.safeParse(
      withTiers([
        { minimumQuantity: 1, maximumQuantity: 4, discountPercent: 0 },
        { minimumQuantity: 7, discountPercent: 5 },
      ]),
    );

    // Named on the band that starts in the wrong place, because that is the number an author
    // fixes — "quantities 5 and 6 are priced by nothing" is not actionable on any other row.
    expect(issuePaths(result)).toContainEqual([
      "quantityItems",
      0,
      "quantityDiscountTiers",
      1,
      "minimumQuantity",
    ]);
  });

  it("rejects overlapping bands", () => {
    const result = createSubscriptionPlanSchema.safeParse(
      withTiers([
        { minimumQuantity: 1, maximumQuantity: 5, discountPercent: 0 },
        { minimumQuantity: 5, discountPercent: 5 },
      ]),
    );

    expect(result.success).toBe(false);
  });

  it("rejects a band left open before the last one", () => {
    const result = createSubscriptionPlanSchema.safeParse(
      withTiers([
        { minimumQuantity: 1, discountPercent: 0 },
        { minimumQuantity: 5, discountPercent: 5 },
      ]),
    );

    expect(issuePaths(result)).toContainEqual([
      "quantityItems",
      0,
      "quantityDiscountTiers",
      0,
      "maximumQuantity",
    ]);
  });

  it("requires the final band to reach a finite item maximum", () => {
    const result = createSubscriptionPlanSchema.safeParse(
      withTiers(
        [
          { minimumQuantity: 1, maximumQuantity: 4, discountPercent: 0 },
          { minimumQuantity: 5, maximumQuantity: 9, discountPercent: 5 },
        ],
        { maxQuantity: 30 },
      ),
    );

    expect(issuePaths(result)).toContainEqual([
      "quantityItems",
      0,
      "quantityDiscountTiers",
      1,
      "maximumQuantity",
    ]);
  });

  it("requires the final band to stay open when the item has no maximum", () => {
    const result = createSubscriptionPlanSchema.safeParse(
      withTiers([
        { minimumQuantity: 1, maximumQuantity: 4, discountPercent: 0 },
        { minimumQuantity: 5, maximumQuantity: 9, discountPercent: 5 },
      ]),
    );

    // Otherwise every quantity above 9 is sold at a price the plan never states.
    expect(issuePaths(result)).toContainEqual([
      "quantityItems",
      0,
      "quantityDiscountTiers",
      1,
      "maximumQuantity",
    ]);
  });

  it("rejects a discount above 100 per cent", () => {
    const result = createSubscriptionPlanSchema.safeParse(
      withTiers([
        { minimumQuantity: 1, maximumQuantity: 4, discountPercent: 0 },
        { minimumQuantity: 5, discountPercent: 140 },
      ]),
    );

    expect(result.success).toBe(false);
  });

  it("rejects a band ending below where it starts", () => {
    const result = createSubscriptionPlanSchema.safeParse(
      withTiers([
        { minimumQuantity: 1, maximumQuantity: 4, discountPercent: 0 },
        { minimumQuantity: 5, maximumQuantity: 2, discountPercent: 5 },
      ]),
    );

    expect(result.success).toBe(false);
  });

  // ---------------------------------------------------------------- fractional quantities

  const meter = (overrides: Record<string, unknown> = {}) => ({
    meterKey: "storage-gb",
    displayName: "Storage",
    unitLabel: "GB",
    aggregation: 0,
    resetPolicy: 0,
    includedQuantity: 500,
    overageAllowed: true,
    thresholdPercents: [],
    rateTables: [],
    ...overrides,
  });

  /**
   * A meter that names no scale parses, and parses as whole units. This is the shape every meter
   * fixture in this file and every plan already stored has, so absent has to mean zero.
   */
  it("defaults a meter with no declared scale to whole units", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [meter()],
    });

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.meters[0].quantityScale).toBe(0);
    }
  });

  it("rejects a fractional allowance on a whole-unit meter", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [meter({ includedQuantity: 512.5 })],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["meters", 0, "includedQuantity"]);
  });

  it("accepts a fractional allowance once the meter declares the places for it", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [meter({ quantityScale: 1, includedQuantity: 512.5 })],
    });

    expect(result.success).toBe(true);
  });

  it("rejects an allowance finer than the declared scale", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [meter({ quantityScale: 2, includedQuantity: 512.005 })],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["meters", 0, "includedQuantity"]);
  });

  it("rejects a scale beyond six places", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [meter({ quantityScale: 7 })],
    });

    expect(result.success).toBe(false);
  });

  /** The band's edge would otherwise sit between two quantities the meter can represent. */
  it("rejects a rate band bound finer than the meter's scale", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [
        meter({
          quantityScale: 1,
          rateTables: [
            {
              currencyCode: "EUR",
              tiers: [{ upToQuantity: 400.05, unitAmount: 1 }],
            },
          ],
        }),
      ],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual([
      "meters",
      0,
      "rateTables",
      0,
      "tiers",
      0,
      "upToQuantity",
    ]);
  });

  it("rejects a carry-forward cap finer than the meter's scale", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [meter({ quantityScale: 1, resetPolicy: 2, carryForwardCap: 50.05 })],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["meters", 0, "carryForwardCap"]);
  });

  /** A grant replaces its meter's allowance, so it is held to that meter's own scale. */
  it("rejects a trial grant finer than its own meter's scale", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [meter({ quantityScale: 1 })],
      trialGrants: [{ meterKey: "storage-gb", includedQuantity: 25.25 }],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["trialGrants", 0, "includedQuantity"]);
  });

  it("accepts a trial grant within its own meter's scale", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [meter({ quantityScale: 2 })],
      trialGrants: [{ meterKey: "storage-gb", includedQuantity: 25.25 }],
    });

    expect(result.success).toBe(true);
  });

  /**
   * A meter's scale governs only its own quantities. A plan mixing a fractional storage meter with
   * a whole-unit screening meter is the case this whole design exists for.
   */
  it("holds each meter to its own scale rather than to the plan's finest", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [
        meter({ quantityScale: 3, includedQuantity: 512.5 }),
        meter({ meterKey: "screening", unitLabel: "screening", includedQuantity: 0.5 }),
      ],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["meters", 1, "includedQuantity"]);
    expect(issuePaths(result)).not.toContainEqual(["meters", 0, "includedQuantity"]);
  });

  /**
   * The exact flow that was broken: a meter with a fractional allowance, and the entitlement that
   * draws it down carrying the same figure.
   *
   * The form fills the limit in itself on selecting the meter, and disables the field while it is
   * inherited — so an integer-only rule here did not merely reject the value, it rejected a value
   * the form had produced and would not let anyone correct. The plan could not be saved at all.
   */
  it("accepts an entitlement limit that matches its meter's fractional allowance", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [meter({ meterKey: "tokens", quantityScale: 2, includedQuantity: 550.55 })],
      entitlements: [
        { key: "tokens", limitKind: 1, limit: 550.55, meterKey: "tokens" },
      ],
    });

    expect(result.success).toBe(true);
  });

  it("rejects an entitlement limit finer than the meter it draws down", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [meter({ meterKey: "tokens", quantityScale: 1, includedQuantity: 550.5 })],
      entitlements: [
        { key: "tokens", limitKind: 1, limit: 550.55, meterKey: "tokens" },
      ],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["entitlements", 0, "limit"]);
  });

  /** A whole-unit meter still refuses a fractional entitlement limit, as it always has. */
  it("rejects a fractional entitlement limit on a whole-unit meter", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [meter({ meterKey: "tokens" })],
      entitlements: [{ key: "tokens", limitKind: 1, limit: 550.55, meterKey: "tokens" }],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["entitlements", 0, "limit"]);
  });

  /**
   * An entitlement naming no meter is a plain cap on something this module does not count, so no
   * meter's scale governs it — only the platform maximum.
   */
  it("holds a meterless entitlement limit only to the platform maximum", () => {
    const allowed = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      entitlements: [{ key: "projects", limitKind: 2, limit: 1.5 }],
    });

    expect(allowed.success).toBe(true);

    const tooFine = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      entitlements: [{ key: "projects", limitKind: 2, limit: 1.00000005 }],
    });

    expect(tooFine.success).toBe(false);
    expect(issuePaths(tooFine)).toContainEqual(["entitlements", 0, "limit"]);
  });
});
