
import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";
import { SSOProviderConfig } from "./sso-provider-config";

type SSOConfigurationProps = {
  params: { provider: SSO_PROVIDERS; id: string };
};
export const SSOConfiguration = ({ params }: SSOConfigurationProps) => {
  return <SSOProviderConfig provider={params.provider} id={params.id} />;
};
