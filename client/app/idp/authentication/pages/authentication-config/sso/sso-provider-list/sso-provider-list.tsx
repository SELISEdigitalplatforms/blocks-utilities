import { useProjectStore } from "@/store/useProjectStore";
import {
  SSOProviderCard,
  SSOProviderCardSkelton,
} from "@blocks-idp/authentication/components/sso-provider-card/sso-provider-card";

import { useMemo } from "react";
import { useGetSsoCredentials } from "@blocks-idp/authentication/hooks/use-sso";
import {
  SOCIAL_AUTH_PROVIDERS_CONFIG,
  SSO_PROVIDERS,
} from "@blocks-idp/authentication/constants/sso-providers.constant";
import { useGetAuthConfig } from "@blocks-idp/authentication/hooks/use-auth-config";

const LoadingSkelton = () => {
  return (
    <div className="grid grid-cols-1 gap-6 text-sm text-low-emphasis sm:grid-cols-2 md:grid-cols-3">
      {Array(6)
        .fill(null)
        .map((_, index) => (
          <SSOProviderCardSkelton key={index} />
        ))}
    </div>
  );
};

export const SSOProviderList = () => {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { isLoading } = useGetAuthConfig({ projectKey: tenantId });

  const { data } = useGetSsoCredentials({ projectKey: tenantId });

  const providers = useMemo(() => {
    const configuredMap = new Map(data?.map((item) => [item.provider, item]));

    return Object.values(SSO_PROVIDERS).map((key) => {
      const base = SOCIAL_AUTH_PROVIDERS_CONFIG[key];
      const config = configuredMap.get(key) || {};
      return {
        ...base,
        ...config,
      };
    });
  }, [data]);

  if (isLoading) return <LoadingSkelton />;

  return (
    <div className="grid grid-cols-1 gap-6 text-sm text-low-emphasis sm:grid-cols-2 md:grid-cols-2 lg:grid-cols-3">
      {providers.map((item) => (
        <SSOProviderCard configuration={item} key={item.provider} />
      ))}
    </div>
  );
};
