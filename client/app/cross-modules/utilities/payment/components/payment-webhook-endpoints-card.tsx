import { Webhook } from "lucide-react";
import { Card } from "@/components/ui-kits/card/card";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { webhookEndpointsFor } from "../utils/webhook-endpoint.util";

interface PaymentWebhookEndpointsCardProps {
  providerName: string;
}

/**
 * The URLs to paste into the provider's own dashboard.
 *
 * Shown rather than documented because this is the one part of setup that happens outside this
 * console, and getting it wrong fails quietly: the provider accepts the configuration, the
 * payment completes for the shopper, and nothing ever tells this service — the payment simply
 * stays in Processing.
 */
export const PaymentWebhookEndpointsCard = ({
  providerName,
}: PaymentWebhookEndpointsCardProps) => {
  const endpoints = webhookEndpointsFor(providerName);

  return (
    <Card className="rounded-xl">
      <div className="flex items-start gap-3">
        <Webhook className="mt-0.5 h-5 w-5 shrink-0 text-blocks-primary-600" />
        <div className="min-w-0 flex-1">
          <h2 className="font-semibold">Webhook endpoints</h2>
          <p className="mt-1 text-sm leading-6 text-muted-foreground">
            Register these in your provider account, then paste the signing
            secret it gives you into the form.
          </p>

          <ul className="mt-4 space-y-4">
            {endpoints.map((endpoint) => (
              <li key={endpoint.url} className="min-w-0">
                <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                  {endpoint.label}
                </p>
                <div className="mt-1 flex items-start gap-2">
                  {/* break-all: these are long and must stay readable rather than
                      overflow the aside, since they are read to be copied. */}
                  <code className="min-w-0 flex-1 break-all rounded bg-muted px-2 py-1 text-xs">
                    {endpoint.url}
                  </code>
                  <CopyToClipboardButton textToCopy={endpoint.url}>
                    <span className="sr-only">Copy {endpoint.label}</span>
                  </CopyToClipboardButton>
                </div>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  {endpoint.hint}
                </p>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </Card>
  );
};
