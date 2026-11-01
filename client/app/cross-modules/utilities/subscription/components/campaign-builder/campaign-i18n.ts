import { tStatic } from "@seliseblocks/genesis-os";

export const CAMPAIGN_I18N_MODULE = "utilities.subscription.campaign-builder";

/**
 * Text for the discount builder from outside a component. Call it when the value is used, never
 * at import time, so a language change is picked up.
 */
export const tCampaign = (key: string, values?: Record<string, string | number>): string =>
  tStatic(CAMPAIGN_I18N_MODULE, key, values);
