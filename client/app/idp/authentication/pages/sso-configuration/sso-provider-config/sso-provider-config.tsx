import { Button } from "@/components/ui-kits/button/button";
import { BookText } from "lucide-react";
import { SsoProviderConfigForms } from "./sso-provider-config-forms/sso-provider-config-forms";
import { SSoProviderSetupGuideLine } from "../sso-provider-setup-guideline";
import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { useState } from "react";

type SSOProviderConfigProps = {
  provider: SSO_PROVIDERS;
  id: string;
};

export const SSOProviderConfig = ({ provider, id = "" }: SSOProviderConfigProps) => {
  const [open, setOpen] = useState<boolean>(false);
  if (!provider) return null;

  BREADCRUMB_CUSTOM_TITLES["/services/authentication?tab=social"] = "Authentication";
  BREADCRUMB_CUSTOM_TITLES[`/services/authentication/sso-configuration`] = provider;
  return (
    <div className="flex flex-col">
      <div className="hidden md:flex">
        <PageBreadcrumb breadcrumbIndex={2} />
      </div>
      <div className="mb-5 flex items-center justify-between rounded text-base">
        <h3 className="text-2xl font-bold tracking-tight">{provider.toUpperCase()}</h3>
        <Button variant="outline" onClick={() => setOpen((open) => !open)}>
          <BookText className="aspect-square w-4" />
          <span className="sr-only sm:not-sr-only sm:ml-2">Setup Guide</span>
        </Button>
      </div>

      <div className="mt-4 flex-1">
        <SsoProviderConfigForms provider={provider} id={id} />
        <SSoProviderSetupGuideLine open={open} onOpenChange={setOpen} provider={provider} />
      </div>
    </div>
  );
};
