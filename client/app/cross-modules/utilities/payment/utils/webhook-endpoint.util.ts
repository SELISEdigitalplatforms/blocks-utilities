import { getRuntimeEnv } from "@/lib/runtime-env";
import { tStatic } from "@seliseblocks/genesis-os";

export interface WebhookEndpoint {
  label: string;
  url: string;
  /** What to enable at the provider, in that provider's own wording. */
  hint: string;
}

/**
 * Webhook paths are mounted without the `/api` prefix the rest of the service uses — the
 * controller carries `[SkipGlobalApiRoutePrefix]` — so these cannot be derived from the API
 * paths the console calls. They are written out literally and pinned by tests.
 */
const WEBHOOK_PATHS = {
  STRIPE: "/payments/stripe/webhooks",
  ADYEN_STANDARD: "/payments/adyen/webhooks/standard",
  ADYEN_TOKENS: "/payments/adyen/webhooks/tokens",
} as const;

/**
 * The public origin this service is reached at.
 *
 * Taken from the same runtime setting every API call uses. In the browser this resolves to
 * the page's origin because the API serves the console from its own wwwroot.
 */
const publicOrigin = (): string => {
  const configured = getRuntimeEnv("BLOCKS_UTILITIES_BASE_URL")?.trim();

  if (configured) {
    return configured.replace(/\/+$/, "");
  }

  return typeof window === "undefined"
    ? ""
    : window.location.origin;
};

/**
 * The endpoints to register in the provider's own dashboard.
 *
 * Adyen has two because it raises stored-payment-method events on a separate notification
 * configuration; Stripe signs everything with one endpoint secret and needs only one.
 */
export const webhookEndpointsFor = (
  providerName: string,
): WebhookEndpoint[] => {
  const origin = publicOrigin();

  if (providerName === "ADYEN-ONLINE") {
    return [
      {
        label: tStatic("utilities.payment", "WEBHOOK_ENDPOINT_UTIL.STANDARD_NOTIFICATIONS"),
        url: `${origin}${WEBHOOK_PATHS.ADYEN_STANDARD}`,
        hint: tStatic("utilities.payment", "WEBHOOK_ENDPOINT_UTIL.CUSTOMER_AREA_DEVELOPERS_WEBHOOKS"),
      },
      {
        label: tStatic("utilities.payment", "WEBHOOK_ENDPOINT_UTIL.TOKEN_NOTIFICATIONS"),
        url: `${origin}${WEBHOOK_PATHS.ADYEN_TOKENS}`,
        hint: tStatic("utilities.payment", "WEBHOOK_ENDPOINT_UTIL.ADD_A_SECOND_WEBHOOK_OF"),
      },
    ];
  }

  return [
    {
      label: tStatic("utilities.payment", "WEBHOOK_ENDPOINT_UTIL.ENDPOINT_URL"),
      url: `${origin}${WEBHOOK_PATHS.STRIPE}`,
      hint: tStatic("utilities.payment", "WEBHOOK_ENDPOINT_UTIL.DEVELOPERS_WEBHOOKS_ADD_ENDPOINT"),
    },
  ];
};
