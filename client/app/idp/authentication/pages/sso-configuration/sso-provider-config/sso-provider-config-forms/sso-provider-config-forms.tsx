import React, { useMemo } from "react";
import { SSOProviderConfigGoogleForm } from "./sso-provider-config-google-form";
import { SSOProviderConfigGithubForm } from "./sso-provider-config-github-form";
import { SSOProviderConfigLinkedINForm } from "./sso-provider-config-linkedin-form";
import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";
import { SSOProviderConfigMicrosoftForm } from "./sso-provider-config-microsoft-form";
import { SSOProviderConfigXForm } from "./sso-provider-config-x-form";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetSsoCredentialById, useSaveSsoCredential } from "@blocks-idp/authentication/hooks/use-sso";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { ISsoProviderConfiguration } from "@blocks-idp/authentication/models/sso.model";
import { useNavigate } from "react-router-dom";
import { SSOProviderConfigOwnSSOForm } from "./sso-provider-config-blocks-own-sso-form";
export type SsoConfigFormsProps = {
  provider: SSO_PROVIDERS;
  id: string;
};

export type SsoConfigForms = {
  configuration: ISsoProviderConfiguration | null;
  save: (data: unknown) => void;
};

export const SsoProviderConfigForms = ({ provider, id }: SsoConfigFormsProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const navigate = useNavigate();
  const { data } = useGetSsoCredentialById({
    itemId: id,
    projectKey: tenantId,
  });

  const { mutateAsync } = useSaveSsoCredential();

  const saveHandler = async (data: ISsoProviderConfiguration) => {
    try {
      const res = await mutateAsync({
        itemId: id && id !== "" ? id : "",
        audience: data.audience,
        clientId: data.clientId,
        clientSecret: data.clientSecret,
        initialPermissions: data.userPermissions?.map((item) => item.resource) || [],
        initialRoles: data.userRoles.map((item) => item.slug) || [],
        provider: data.provider,
        redirectUrl: data.redirectUrl,
        projectKey: tenantId,
      });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      if (!id) navigate(`/services/authentication/sso-configuration?provider=${provider}&id=${res.itemId}`);
      showSuccessToast({ description: `${provider} is configured successfully` });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  };
  const CurrentForm = useMemo(() => {
    switch (provider) {
      case SSO_PROVIDERS.google:
        return SSOProviderConfigGoogleForm;
      case SSO_PROVIDERS.microsoft:
        return SSOProviderConfigMicrosoftForm;
      case SSO_PROVIDERS.ownsso:
        return SSOProviderConfigOwnSSOForm;
      case SSO_PROVIDERS.github:
        return SSOProviderConfigGithubForm;
      case SSO_PROVIDERS.linkedin:
        return SSOProviderConfigLinkedINForm;
      case SSO_PROVIDERS.x:
        return SSOProviderConfigXForm;
      default:
        return null;
    }
  }, [provider]);

  if (!CurrentForm) return null;
  // eslint-disable-next-line @typescript-eslint/ban-ts-comment
  //@ts-expect-error
  return <CurrentForm save={saveHandler} configuration={data || null} />;
};
