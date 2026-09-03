import type { FieldErrors, FieldPath } from "react-hook-form";

import type { CreateSubscriptionPlanFormValues } from "../schemas/subscription-plan.schema";

/**
 * Which step of the plan builder owns each field, so a failed save can send the author to the
 * problem instead of describing it.
 *
 * A `Record` over every key of the form values rather than a lookup that tolerates a miss: adding
 * a field to the schema without saying which step shows it stops compiling, which is the only way
 * this mapping stays true. A field silently unmapped would be worse than no mapping at all — the
 * author would be told something is wrong and sent nowhere.
 *
 * Declared in the order each step lays its fields out. Iteration order of string keys is insertion
 * order, so the first error found walking this is the first one an author would come to on screen
 * rather than whichever react-hook-form happened to record first.
 */
export const PLAN_BUILDER_STEP_OF_FIELD: Record<
  keyof CreateSubscriptionPlanFormValues,
  number
> = {
  // 1 — Identity
  code: 1,
  displayName: 1,
  description: 1,
  organizationId: 1,
  familyCode: 1,
  familyRank: 1,
  featuresJson: 1,

  // 2 — Pricing model
  quantityItems: 2,
  prices: 2,
  quantityDiscountCombinationPolicy: 2,
  usageInterval: 2,
  usageIntervalCount: 2,
  meters: 2,
  requirePaymentMethodUpfront: 2,

  // 3 — What the plan grants
  entitlements: 3,

  // 4 — Trial
  trialDurationKind: 4,
  trialDurationCount: 4,
  trialRequiresPaymentMethod: 4,
  trialGrants: 4,
};

/** A field path this form can focus. */
export type PlanBuilderFieldPath = FieldPath<CreateSubscriptionPlanFormValues>;

/** The step titles, so a message can name where the author is being sent. */
export const PLAN_BUILDER_STEP_TITLES: Record<number, string> = {
  1: "Identity",
  2: "Pricing model",
  3: "What the plan grants",
  4: "Trial",
  5: "Review",
};

/**
 * The first field carrying an error, as a path react-hook-form can focus.
 *
 * Walks down to the leaf that actually holds the message, because that is the control with a DOM
 * node: `meters` itself is not focusable, `meters.1.includedQuantity` is. An error attached to an
 * array as a whole — "add at least one price" — has no leaf below it, so the array's own name is
 * returned and focusing lands on its first control.
 */
export const firstPlanBuilderErrorField = (
  errors: FieldErrors<CreateSubscriptionPlanFormValues>,
): PlanBuilderFieldPath | undefined => {
  for (const field of Object.keys(PLAN_BUILDER_STEP_OF_FIELD)) {
    const error = (errors as Record<string, unknown>)[field];

    if (error) {
      // Asserted rather than proven. The path is assembled from react-hook-form's own error tree,
      // whose shape mirrors the form values, so every path this can produce is a path that form
      // has — but the indices are runtime values and the template type cannot know them. A wrong
      // path would make setFocus a no-op, not a crash, and the step change has already happened.
      return `${field}${leafPath(error)}` as PlanBuilderFieldPath;
    }
  }

  return undefined;
};

/** The earliest step holding an error, or undefined when nothing does. */
export const firstPlanBuilderErrorStep = (
  errors: FieldErrors<CreateSubscriptionPlanFormValues>,
): number | undefined => {
  const steps = Object.entries(PLAN_BUILDER_STEP_OF_FIELD)
    .filter(([field]) => Boolean((errors as Record<string, unknown>)[field]))
    .map(([, step]) => step);

  return steps.length > 0 ? Math.min(...steps) : undefined;
};

/**
 * Descends an error node to the leaf that owns the message.
 *
 * A leaf is recognised by carrying its own `message` or `type` — the shape react-hook-form gives a
 * single field's error — rather than by depth, because an array error node holds both indexed
 * children and, when the array itself failed, a `root`-like message beside them.
 */
const leafPath = (node: unknown): string => {
  if (!isRecord(node) || isLeaf(node)) {
    return "";
  }

  for (const [key, child] of Object.entries(node)) {
    // Bookkeeping react-hook-form attaches beside the real children, none of it focusable.
    // `root` is where it files an error belonging to an array as a whole; skipping it leaves the
    // array's own name as the path, which focuses that array's first control.
    if (BOOKKEEPING.has(key)) {
      continue;
    }

    if (isRecord(child)) {
      return `.${key}${leafPath(child)}`;
    }
  }

  return "";
};

const BOOKKEEPING = new Set(["ref", "types", "message", "type", "root"]);

const isLeaf = (node: Record<string, unknown>): boolean =>
  typeof node.message === "string" || typeof node.type === "string";

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === "object" && value !== null;
