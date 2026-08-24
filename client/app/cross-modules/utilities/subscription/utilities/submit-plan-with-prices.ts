import type {
  CreateSubscriptionPriceRequest,
  SubscriptionPlan,
} from "../models/subscription-plan.model";
import type { CreateSubscriptionPriceFormValues } from "../schemas/subscription-price.schema";
import { FLAT_FEE } from "../schemas/subscription-price.schema";
import { toMinorUnits } from "./subscription-format";
import { toBasisPoints } from "./subscription-tax";

export interface PlanSubmissionResult {
  plan: SubscriptionPlan;
  /** One line per price that did not land, in the words the author will see. */
  failures: string[];
}

/**
 * Creates a plan and then its prices.
 *
 * Two calls rather than one, because a price cannot be posted until the plan it belongs to has an
 * id. That ordering is the whole reason this exists: once the plan is created it stays created,
 * so a price that fails must not be reported as a failed submission — resubmitting would only
 * collide on the plan code, and the author would have lost the plan they already have. A failing
 * price is collected and reported instead, leaving the rest to be added from the plan page.
 *
 * A failing *plan* is different: nothing was created, so it throws and the caller keeps the form.
 */
export const submitPlanWithPrices = async <TPlanRequest,>({
  planRequest,
  prices,
  createPlan,
  createPrice,
}: {
  planRequest: TPlanRequest;
  prices: CreateSubscriptionPriceFormValues[];
  /** Creating or saving — both return the plan, and both are followed by the same price loop. */
  createPlan: (request: TPlanRequest) => Promise<SubscriptionPlan>;
  createPrice: (request: CreateSubscriptionPriceRequest) => Promise<SubscriptionPlan>;
}): Promise<PlanSubmissionResult> => {
  const plan = await createPlan(planRequest);
  const failures: string[] = [];

  for (const [index, price] of prices.entries()) {
    try {
      await createPrice({
        planId: plan.planId,
        // The plan may belong to an organization the console is not itself in, and the server
        // resolves each request on its own — without naming it, the plan reads as missing.
        organizationId: plan.organizationId ?? undefined,
        currencyCode: price.currencyCode,
        unitAmountMinor: toMinorUnits(price.amount, price.currencyCode),
        interval: price.interval,
          intervalCount: price.intervalCount,
          displayPriceNote: price.displayPriceNote?.trim() || undefined,
        quantityItemKey:
          price.quantityItemKey === FLAT_FEE ? undefined : price.quantityItemKey,
        // Both or neither. The server refuses a rate without a mode — deliberately, since the same
        // number means two different prices — and a mode without a rate would describe a tax that
        // does not apply.
        taxRateBasisPoints: price.taxPercent ? toBasisPoints(price.taxPercent) : undefined,
        taxMode: price.taxPercent ? price.taxMode : undefined,
      });
    } catch (error) {
      failures.push(
        `Price ${index + 1} (${price.currencyCode} ${price.amount}): ${
          error instanceof Error ? error.message : "could not be added"
        }`,
      );
    }
  }

  return { plan, failures };
};
