import type { FieldErrors } from "react-hook-form";
import { describe, expect, it } from "vitest";

import {
  firstPlanBuilderErrorField,
  firstPlanBuilderErrorStep,
  PLAN_BUILDER_STEP_OF_FIELD,
  PLAN_BUILDER_STEP_TITLES,
} from "./plan-builder-steps";
import {
  createSubscriptionPlanSchema,
  defaultSubscriptionPlanFormValues,
} from "../schemas/subscription-plan.schema";
import type { CreateSubscriptionPlanFormValues } from "../schemas/subscription-plan.schema";

const errors = (shape: unknown) => shape as FieldErrors<CreateSubscriptionPlanFormValues>;

/** The shape react-hook-form gives one field's error. */
const leaf = (message: string) => ({ type: "custom", message, ref: undefined });

describe("the field-to-step map", () => {
  /**
   * Every field the form holds is on a step.
   *
   * The `Record` type already makes a missing field a compile error, so this guards the other
   * direction: a mapping that names a field the form no longer has, which would compile forever
   * and quietly send authors to a step that does not show it.
   */
  it("names exactly the fields the form has", () => {
    const formFields = Object.keys(defaultSubscriptionPlanFormValues).sort();
    const mappedFields = Object.keys(PLAN_BUILDER_STEP_OF_FIELD).sort();

    expect(mappedFields).toEqual(formFields);
  });

  it("puts every field on a step the builder actually renders", () => {
    for (const step of Object.values(PLAN_BUILDER_STEP_OF_FIELD)) {
      expect(PLAN_BUILDER_STEP_TITLES[step]).toBeDefined();
    }
  });

  /** The review step shows no fields, so nothing may be mapped to it. */
  it("maps nothing to the review step", () => {
    expect(Object.values(PLAN_BUILDER_STEP_OF_FIELD)).not.toContain(5);
  });
});

describe("firstPlanBuilderErrorStep", () => {
  it("is undefined when nothing is wrong", () => {
    expect(firstPlanBuilderErrorStep(errors({}))).toBeUndefined();
  });

  it.each([
    ["code", 1],
    ["meters", 2],
    ["prices", 2],
    ["entitlements", 3],
    ["trialGrants", 4],
  ])("sends %s to step %s", (field, step) => {
    expect(firstPlanBuilderErrorStep(errors({ [field]: leaf("bad") }))).toBe(step);
  });

  /**
   * The earliest step, so an author fixing things walks forward rather than being bounced
   * backwards on each attempt.
   */
  it("picks the earliest step when several have errors", () => {
    const step = firstPlanBuilderErrorStep(
      errors({
        trialGrants: leaf("bad"),
        entitlements: leaf("bad"),
        displayName: leaf("bad"),
      }),
    );

    expect(step).toBe(1);
  });
});

describe("firstPlanBuilderErrorField", () => {
  it("is undefined when nothing is wrong", () => {
    expect(firstPlanBuilderErrorField(errors({}))).toBeUndefined();
  });

  it("names a plain field", () => {
    expect(firstPlanBuilderErrorField(errors({ displayName: leaf("bad") }))).toBe("displayName");
  });

  /**
   * Descends to the control that owns the message. `meters` is not focusable; the input inside it
   * is, and it is the thing the author has to change.
   */
  it("descends into an array to the field that failed", () => {
    const field = firstPlanBuilderErrorField(
      errors({ meters: [undefined, { includedQuantity: leaf("whole numbers only") }] }),
    );

    expect(field).toBe("meters.1.includedQuantity");
  });

  it("descends through a nested array", () => {
    const field = firstPlanBuilderErrorField(
      errors({
        meters: [
          {
            rateTables: [{ tiers: [{ upToQuantity: leaf("too fine") }] }],
          },
        ],
      }),
    );

    expect(field).toBe("meters.0.rateTables.0.tiers.0.upToQuantity");
  });

  /**
   * An error belonging to an array as a whole has no control of its own, so the array's name is
   * the best available target and focus lands on its first control.
   */
  it("stops at the array when the array itself is what failed", () => {
    expect(firstPlanBuilderErrorField(errors({ prices: leaf("Add at least one price.") }))).toBe(
      "prices",
    );
  });

  it("ignores the bookkeeping react-hook-form files beside real children", () => {
    const field = firstPlanBuilderErrorField(
      errors({
        prices: Object.assign([{ amount: leaf("bad") }], {
          root: leaf("the list as a whole"),
        }),
      }),
    );

    expect(field).toBe("prices.0.amount");
  });

  /** Declaration order is the order a step lays its fields out, not react-hook-form's. */
  it("returns the earliest field on screen rather than the first recorded", () => {
    const field = firstPlanBuilderErrorField(
      errors({ familyRank: leaf("bad"), displayName: leaf("bad"), code: leaf("bad") }),
    );

    expect(field).toBe("code");
  });
});

/**
 * The map is only useful if the paths it is fed are the paths the schema actually produces, so
 * these drive it from real validation output rather than from hand-built error trees.
 */
describe("against real validation output", () => {
  const invalidPlan = (overrides: Record<string, unknown>) => ({
    ...defaultSubscriptionPlanFormValues,
    code: "pro",
    displayName: "Pro",
    ...overrides,
  });

  const errorTree = (values: Record<string, unknown>) => {
    const result = createSubscriptionPlanSchema.safeParse(values);

    expect(result.success).toBe(false);

    // Mirrors how zodResolver nests issue paths into the shape react-hook-form holds.
    const tree: Record<string, unknown> = {};

    if (!result.success) {
      for (const issue of result.error.issues) {
        let node = tree;

        issue.path.slice(0, -1).forEach((segment) => {
          node[segment] ??= {};
          node = node[segment] as Record<string, unknown>;
        });

        node[issue.path[issue.path.length - 1]] = leaf(issue.message);
      }
    }

    return errors(tree);
  };

  it("sends a missing display name to Identity", () => {
    const tree = errorTree(invalidPlan({ displayName: "" }));

    expect(firstPlanBuilderErrorStep(tree)).toBe(1);
    expect(firstPlanBuilderErrorField(tree)).toBe("displayName");
  });

  /**
   * The case that prompted this: a fractional quantity on a whole-unit meter is reported three
   * steps away from the review step the author pressed Save on.
   */
  it("sends a fractional allowance on a whole-unit meter to Pricing model", () => {
    const tree = errorTree(
      invalidPlan({
        meters: [
          {
            meterKey: "storage-gb",
            displayName: "Storage",
            unitLabel: "GB",
            aggregation: 0,
            resetPolicy: 0,
            includedQuantity: 512.5,
            overageAllowed: true,
            thresholdPercents: [],
            rateTables: [],
          },
        ],
      }),
    );

    expect(firstPlanBuilderErrorStep(tree)).toBe(2);
    expect(firstPlanBuilderErrorField(tree)).toBe("meters.0.includedQuantity");
  });

  it("sends a counted entitlement with no meter to What the plan grants", () => {
    const tree = errorTree(
      invalidPlan({
        entitlements: [{ key: "usage", limitKind: 1 }],
      }),
    );

    expect(firstPlanBuilderErrorStep(tree)).toBe(3);
  });
});
