import { getRuntimeEnv } from "@/lib/runtime-env";

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
        label: "Standard notifications",
        url: `${origin}${WEBHOOK_PATHS.ADYEN_STANDARD}`,
        hint: "Customer Area → Developers → Webhooks → Standard notification",
      },
      {
        label: "Token notifications",
        url: `${origin}${WEBHOOK_PATHS.ADYEN_TOKENS}`,
        hint: "Add a second webhook of type Tokenization, for saved cards",
      },
    ];
  }

  return [
    {
      label: "Endpoint URL",
      url: `${origin}${WEBHOOK_PATHS.STRIPE}`,
      hint: "Developers → Webhooks → Add endpoint, then copy its signing secret below",
    },
  ];
};
