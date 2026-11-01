import { registerTranslations } from "@seliseblocks/genesis-os";

import m_console from "./utilities.console.en.json";
import m_idp from "./utilities.idp.en.json";
import m_idp_authentication from "./utilities.idp.authentication.en.json";
import m_magic_url from "./utilities.magic-url.en.json";
import m_payment from "./utilities.payment.en.json";
import m_shared from "./utilities.shared.en.json";
import m_sms from "./utilities.sms.en.json";
import m_subscription from "./utilities.subscription.en.json";
import m_subscription_simulation from "./utilities.subscription-simulation.en.json";
import m_subscription_campaign_builder from "./utilities.subscription.campaign-builder.en.json";
import m_subscription_pages from "./utilities.subscription.pages.en.json";
import m_subscription_plan_builder from "./utilities.subscription.plan-builder.en.json";

// English text of every module. German comes from the localization service.
const dictionaries: Record<string, Record<string, string>> = {
  "utilities.console": m_console,
  "utilities.idp": m_idp,
  "utilities.idp.authentication": m_idp_authentication,
  "utilities.magic-url": m_magic_url,
  "utilities.payment": m_payment,
  "utilities.shared": m_shared,
  "utilities.sms": m_sms,
  "utilities.subscription": m_subscription,
  "utilities.subscription-simulation": m_subscription_simulation,
  "utilities.subscription.campaign-builder": m_subscription_campaign_builder,
  "utilities.subscription.pages": m_subscription_pages,
  "utilities.subscription.plan-builder": m_subscription_plan_builder,
};

for (const [module, dictionary] of Object.entries(dictionaries)) {
  registerTranslations(module, dictionary);
}
