import type {
  CreateSubscriptionPriceRequest,
  SubscriptionPlan,
} from "../models/subscription-plan.model";
import type { CreateSubscriptionPriceFormValues } from "../schemas/subscription-price.schema";
import { createPricesInTurn } from "./create-price-request";

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

  const failures = await createPricesInTurn({
    prices,
    planId: plan.planId,
    organizationId: plan.organizationId ?? undefined,
    createPrice,
  });

  return { plan, failures };
};
