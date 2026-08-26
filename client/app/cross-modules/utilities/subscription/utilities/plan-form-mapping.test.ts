import { describe, expect, it } from "vitest";
import { TENANT_WIDE_ORGANIZATION } from "../constants/subscription.constants";
import type { SubscriptionPlan } from "../models/subscription-plan.model";
import { createSubscriptionPlanSchema } from "../schemas/subscription-plan.schema";
import { planToFormValues, toUpdatePlanRequest } from "./plan-form-mapping";

const storedPlan = (overrides: Partial<SubscriptionPlan> = {}): SubscriptionPlan => ({
  planId: "plan-1",
  code: "starter",
  displayName: "Starter",
  description: "For small teams",
  featuresJson: null,
  organizationId: "org-1",
  trialDays: 1,
  trialRequiresPaymentMethod: true,
  version: 1,
  hasSubscribers: false,
  quantityItems: [
    {
      itemKey: "seat",
      unitLabel: "seat",
      minQuantity: 1,
      maxQuantity: 50,
      defaultQuantity: 1,
    },
  ],
  meters: [
    {
      meterKey: "ses-signatures",
      displayName: "Simple Signatures",
      unitLabel: "signature",
      aggregation: "Sum",
      resetPolicy: "Periodic",
      includedQuantity: 150,
      overageAllowed: true,
      thresholdPercents: [80, 100],
      rateTables: [
        {
          currencyCode: "EUR",
          tiers: [
            { upToQuantity: 1000, unitAmountMinor: 5 },
            { upToQuantity: null, unitAmountMinor: 3 },
          ],
        },
      ],
    },
  ],
  entitlements: [
    {
      key: "ses-signatures",
      limitKind: "Count",
      limit: 150,
      meterKey: "ses-signatures",
      unitLabel: "signature",
    },
  ],
  prices: [
    {
      priceId: "price-1",
      currencyCode: "EUR",
      unitAmountMinor: 300,
      interval: "Month",
      intervalCount: 1,
      quantityItemKey: null,
    },
  ],
  trialGrants: [{ meterKey: "ses-signatures", includedQuantity: 5 }],
  ...overrides,
});

describe("planToFormValues", () => {
  it("turns response enum names back into the numbers the form and request use", () => {
    const values = planToFormValues(storedPlan());

    expect(values.meters[0].aggregation).toBe(0);
    expect(values.meters[0].resetPolicy).toBe(0);
    expect(values.entitlements[0].limitKind).toBe(1);
  });

  it("maps a never-reset response to the numeric request enum", () => {
    const plan = storedPlan();
    plan.meters[0] = {
      ...plan.meters[0],
      resetPolicy: "Never",
      overageAllowed: false,
      rateTables: [],
    };

    expect(planToFormValues(plan).meters[0].resetPolicy).toBe(1);
  });

  it("keeps trial grants, which an edit would otherwise drop", () => {
    expect(planToFormValues(storedPlan()).trialGrants).toEqual([
      { meterKey: "ses-signatures", includedQuantity: 5 },
    ]);
  });

  it("starts with no price rows, because saving does not touch existing prices", () => {
    expect(planToFormValues(storedPlan()).prices).toEqual([]);
  });

  it("reads a tenant-wide plan back as the sentinel the picker uses", () => {
    expect(planToFormValues(storedPlan({ organizationId: null })).organizationId).toBe(
      TENANT_WIDE_ORGANIZATION,
    );
  });

  it("carries an unbounded final tier across as undefined, not null", () => {
    const table = planToFormValues(storedPlan()).meters[0].rateTables[0];

    expect(table.tiers[1].upToQuantity).toBeUndefined();
  });

  it("produces a draft the create schema still accepts, apart from the price it now lacks", () => {
    const values = planToFormValues(storedPlan());
    const result = createSubscriptionPlanSchema.safeParse({
      ...values,
      prices: [
        {
          currencyCode: "EUR",
          amount: 3,
          interval: 2,
          intervalCount: 1,
          quantityItemKey: "seat",
        },
      ],
    });

    expect(result.success).toBe(true);
  });
});

describe("toUpdatePlanRequest", () => {
  it("names the plan's own organization so the server can find it", () => {
    const request = toUpdatePlanRequest(planToFormValues(storedPlan()), "org-1");

    expect(request.organizationId).toBe("org-1");
  });

  it("omits the organization for a tenant-wide plan", () => {
    const request = toUpdatePlanRequest(planToFormValues(storedPlan()), null);

    expect(request.organizationId).toBeUndefined();
  });

  it("sends no code, which the server takes from the stored plan", () => {
    const request = toUpdatePlanRequest(planToFormValues(storedPlan()), "org-1");

    expect(request).not.toHaveProperty("code");
  });

  it("round-trips a plan's terms unchanged when nothing was edited", () => {
    const request = toUpdatePlanRequest(planToFormValues(storedPlan()), "org-1");

    expect(request.displayName).toBe("Starter");
    expect(request.meters[0]).toMatchObject({
      meterKey: "ses-signatures",
      aggregation: 0,
      resetPolicy: 0,
      includedQuantity: 150,
      thresholdPercents: [80, 100],
    });
    expect(request.trialGrants).toEqual([{ meterKey: "ses-signatures", includedQuantity: 5 }]);
  });
});

describe("quantity discount bands", () => {
  const stored = (
    tiers?: { minimumQuantity: number; maximumQuantity: number | null; discountBasisPoints: number }[],
  ) =>
    storedPlan({
      quantityItems: [
        {
          itemKey: "user",
          unitLabel: "user",
          minQuantity: 1,
          maxQuantity: null,
          defaultQuantity: 1,
          ...(tiers ? { quantityDiscountTiers: tiers } : {}),
        },
      ],
    });

  it("reopens basis points as the percentages an author typed", () => {
    const values = planToFormValues(
      stored([
        { minimumQuantity: 1, maximumQuantity: 4, discountBasisPoints: 0 },
        { minimumQuantity: 5, maximumQuantity: 9, discountBasisPoints: 500 },
        { minimumQuantity: 10, maximumQuantity: null, discountBasisPoints: 2000 },
      ]),
    );

    expect(values.quantityItems[0].quantityDiscountTiers).toEqual([
      { minimumQuantity: 1, maximumQuantity: 4, discountPercent: 0 },
      { minimumQuantity: 5, maximumQuantity: 9, discountPercent: 5 },
      { minimumQuantity: 10, maximumQuantity: undefined, discountPercent: 20 },
    ]);
  });

  it("reopens a plan stored before bands existed with the control off", () => {
    expect(planToFormValues(stored()).quantityItems[0].quantityDiscountTiers).toEqual([]);
  });

  it("sends percentages back as basis points", () => {
    const request = toUpdatePlanRequest(
      planToFormValues(
        stored([
          { minimumQuantity: 1, maximumQuantity: 4, discountBasisPoints: 0 },
          { minimumQuantity: 5, maximumQuantity: 9, discountBasisPoints: 500 },
          { minimumQuantity: 10, maximumQuantity: null, discountBasisPoints: 1250 },
        ]),
      ),
      "org-1",
    );

    // The round trip is the half that makes editing work: stored, reopened, submitted again, the
    // numbers have to be the ones that were authored. Without it the server keeps the bands and
    // the builder reopens as though they were never there.
    expect(request.quantityItems[0].quantityDiscountTiers).toEqual([
      { minimumQuantity: 1, maximumQuantity: 4, discountBasisPoints: 0 },
      { minimumQuantity: 5, maximumQuantity: 9, discountBasisPoints: 500 },
      { minimumQuantity: 10, maximumQuantity: undefined, discountBasisPoints: 1250 },
    ]);
  });

  it("carries an edited percentage through as basis points", () => {
    const values = planToFormValues(
      stored([
        { minimumQuantity: 1, maximumQuantity: 4, discountBasisPoints: 0 },
        { minimumQuantity: 5, maximumQuantity: null, discountBasisPoints: 500 },
      ]),
    );

    values.quantityItems[0].quantityDiscountTiers[1].discountPercent = 6;

    const request = toUpdatePlanRequest(values, "org-1");

    expect(request.quantityItems[0].quantityDiscountTiers?.[1].discountBasisPoints).toBe(600);
  });

  it("omits the field entirely when no bands are authored", () => {
    const request = toUpdatePlanRequest(planToFormValues(stored()), "org-1");

    // Not an empty array: absent and empty mean the same thing to the API, and sending one makes
    // a plan that never had bands look like one whose bands were removed.
    expect(request.quantityItems[0].quantityDiscountTiers).toBeUndefined();
  });
});

describe("quantity discount combination policy", () => {
  it("reopens Stack as Stack and submits it unchanged", () => {
    // The bug this guards: the field was absent from the client entirely, so every edit submitted
    // nothing and the server reset the plan to BestDiscount. A plan authored to compound quietly
    // stopped compounding the first time anyone opened it in the builder.
    const values = planToFormValues(
      storedPlan({ quantityDiscountCombinationPolicy: "Stack" }),
    );

    expect(values.quantityDiscountCombinationPolicy).toBe(2);
    expect(toUpdatePlanRequest(values, "org-1").quantityDiscountCombinationPolicy).toBe(2);
  });

  it("reopens QuantityOnly as QuantityOnly", () => {
    const values = planToFormValues(
      storedPlan({ quantityDiscountCombinationPolicy: "QuantityOnly" }),
    );

    expect(values.quantityDiscountCombinationPolicy).toBe(1);
    expect(toUpdatePlanRequest(values, "org-1").quantityDiscountCombinationPolicy).toBe(1);
  });

  it("treats a plan stored before the policy existed as the safe default", () => {
    const values = planToFormValues(storedPlan());

    expect(values.quantityDiscountCombinationPolicy).toBe(0);
  });

  it("always sends a policy, so the server never defaults one", () => {
    const request = toUpdatePlanRequest(planToFormValues(storedPlan()), "org-1");

    expect(request.quantityDiscountCombinationPolicy).toBeDefined();
  });
});

describe("automatic price discounts", () => {
  it("reopens a duplicated price with its discount and combination intact", () => {
    // Duplicating exists so somebody can start from a plan they already sell. A duplicate that
    // dropped the 8% yearly discount would quietly author a different product.
    const values = planToFormValues(
      storedPlan({
        prices: [
          {
            priceId: "price-yearly",
            currencyCode: "CHF",
            unitAmountMinor: 100_000,
            interval: "Year",
            intervalCount: 1,
            quantityItemKey: null,
            automaticDiscountBasisPoints: 800,
            quantityDiscountCombination: "Additive",
          },
        ],
      }),
      { includePrices: true },
    );

    expect(values.prices[0].automaticDiscountPercent).toBe(8);
    expect(values.prices[0].quantityDiscountCombination).toBe("Additive");
  });

  it("reads a discounted price with no combination as the safe one", () => {
    // How the server calculates it, so a duplicate cannot start giving the volume band away too.
    const values = planToFormValues(
      storedPlan({
        prices: [
          {
            priceId: "price-yearly",
            currencyCode: "CHF",
            unitAmountMinor: 100_000,
            interval: "Year",
            intervalCount: 1,
            quantityItemKey: null,
            automaticDiscountBasisPoints: 800,
          },
        ],
      }),
      { includePrices: true },
    );

    expect(values.prices[0].quantityDiscountCombination).toBe("BestDiscount");
  });

  it("leaves an undiscounted price without one", () => {
    const values = planToFormValues(
      storedPlan({
        prices: [
          {
            priceId: "price-monthly",
            currencyCode: "CHF",
            unitAmountMinor: 10_000,
            interval: "Month",
            intervalCount: 1,
            quantityItemKey: null,
          },
        ],
      }),
      { includePrices: true },
    );

    expect(values.prices[0].automaticDiscountPercent).toBeUndefined();
  });
});

describe("requiring a card before activation", () => {
  it("survives the trip through the form and back onto an edit", () => {
    const values = planToFormValues(storedPlan({ requirePaymentMethodUpfront: true }));

    expect(values.requirePaymentMethodUpfront).toBe(true);
    expect(toUpdatePlanRequest(values).requirePaymentMethodUpfront).toBe(true);
  });

  /**
   * A plan stored before the setting existed answers with nothing at all. Reading that as "leave
   * it as it was" would turn an edit — or a duplicate — into a plan that suddenly demands a card.
   */
  it("reads an older plan that never had the field as off", () => {
    const values = planToFormValues(storedPlan());

    expect(values.requirePaymentMethodUpfront).toBe(false);
    expect(toUpdatePlanRequest(values).requirePaymentMethodUpfront).toBe(false);
  });
});
