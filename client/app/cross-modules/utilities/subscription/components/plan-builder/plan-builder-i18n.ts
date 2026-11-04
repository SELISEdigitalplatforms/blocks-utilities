import { tStatic } from "@seliseblocks/genesis-os";

export const PLAN_BUILDER_I18N_MODULE = "utilities.subscription.plan-builder";

/**
 * Text for the plan builder from outside a component. Call it when the value is used, never at
 * import time, so a language change is picked up.
 */
export const tPlanBuilder = (key: string, values?: Record<string, string | number>): string =>
  tStatic(PLAN_BUILDER_I18N_MODULE, key, values);
