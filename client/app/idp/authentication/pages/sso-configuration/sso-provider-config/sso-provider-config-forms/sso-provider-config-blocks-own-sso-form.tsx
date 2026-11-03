import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Form } from "@/components/ui-kits/form/form";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useProjectStore, useT, tStatic } from "@seliseblocks/genesis-os";
import { isParsableUrl } from "@/lib/url-format";

import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";
import { useSaveSsoCredential } from "@blocks-idp/authentication/hooks/use-sso";
import { ISsoProviderConfiguration } from "@blocks-idp/authentication/models/sso.model";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm, type Resolver } from "react-hook-form";
import { z } from "zod";
import { createCommonOAuthFields } from "../sso-provider-config-field-factory.util";
import { createSsoProviderConfigBaseSchema } from "../sso-provider-config.schema";
import { SSOProviderConfigFormFieldType } from "../sso-provider-config.type";
import { SSOProviderConfigFormField } from "./sso-provider-config-form-fields";
import { SsoConfigForms } from "./sso-provider-config-forms";

const UTILITIES_IDP_AUTHENTICATION_MODULE = "utilities.idp.authentication";

const getSsoOwnSsoFormFields = (): SSOProviderConfigFormFieldType[] => [
  ...createCommonOAuthFields({ clientId: { description: "" } }),
  {
    id: "6",
    label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "SSO_PROVIDER_CONFIG_BLOCKS_OWN_SSO_FORM.WELL_KNOWN_URL"),
    type: "input",
    name: "wellKnownUrl",
  },
];

type FormValue = {
  provider: string;
  audience: string;
  clientId: string;
  clientSecret: string;
  redirectUrl: string;
  wellKnownUrl: string;
};

const createSchema = () => createSsoProviderConfigBaseSchema().extend({
  clientId: z.string().trim().nonempty(tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "SSO_PROVIDER_CONFIG_BLOCKS_OWN_SSO_FORM.CLIENT_ID_IS_REQUIRED")),
  clientSecret: z.string().trim().nonempty(tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "SSO_PROVIDER_CONFIG_BLOCKS_OWN_SSO_FORM.CLIENT_SECRET_IS_REQUIRED")),
  wellKnownUrl: z.string().refine(isParsableUrl, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "SSO_PROVIDER_CONFIG_BLOCKS_OWN_SSO_FORM.WELL_KNOWN_URL_MUST_BE")).trim(),
});

export const SSOProviderConfigOwnSSOForm: React.FC<SsoConfigForms> = ({ configuration }) => {
  const { t } = useT(UTILITIES_IDP_AUTHENTICATION_MODULE);
  const projectKey = useProjectStore()?.selectedProject?.tenantId || "";
  const { mutateAsync } = useSaveSsoCredential();

  const values = configuration || {
    provider: SSO_PROVIDERS.ownsso,
    audience: "",
    clientId: "",
    clientSecret: "",
    redirectUrl: "",
    wellKnownUrl: "",
  };
  const form = useForm({
    values,
    resolver: zodResolver(createSchema()) as unknown as Resolver<typeof values>,
  });

  const onFormSubmit = async (data: ISsoProviderConfiguration | FormValue) => {
    const payload = {
      provider: data.provider,
      audience: data.audience,
      clientId: data.clientId,
      clientSecret: data.clientSecret,
      redirectUrl: data.redirectUrl,
      wellKnownUrl: data.wellKnownUrl,
      projectKey: projectKey,
      initialRoles: [],
      initialPermissions: [],
      ssoType: 1,
    };

    const res = await mutateAsync(payload);

    if (!res.isSuccess) return showErrorToast({ errors: res.errors });
    showSuccessToast({ description: t("SSO_PROVIDER_CONFIG_BLOCKS_OWN_SSO_FORM.BRING_YOUR_OWN_SSO_IS") });
  };

  return (
    <Form {...form}>
      <form
        className="flex h-full flex-col justify-between gap-6"
        onSubmit={form.handleSubmit(onFormSubmit)}
      >
        <Card>
          <CardHeader>
            <CardTitle>{t("SSO_PROVIDER_CONFIG_BLOCKS_OWN_SSO_FORM.GENERAL")}</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <SSOProviderConfigFormField fields={getSsoOwnSsoFormFields()} form={form} />
          </CardContent>
        </Card>

        <div className="flex items-center justify-end gap-2">
          <Button type="submit">{t("common:SAVE")}</Button>
        </div>
      </form>
    </Form>
  );
};
