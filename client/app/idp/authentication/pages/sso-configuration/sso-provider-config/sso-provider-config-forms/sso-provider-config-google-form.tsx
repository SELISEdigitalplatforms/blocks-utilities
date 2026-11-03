import { Link } from "react-router";
import React from "react";
import { useForm, type Resolver } from "react-hook-form";

import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Form, FormField } from "@/components/ui-kits/form/form";
import { SSOInitialPermissions } from "@blocks-idp/authentication/components/sso-initial-permissions";
import { SSOInitialRoles } from "@blocks-idp/authentication/components/sso-initial-roles";
import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";
import { zodResolver } from "@hookform/resolvers/zod";
import { createCommonOAuthFields } from "../sso-provider-config-field-factory.util";
import { createSsoOAuthProviderSchema } from "../sso-provider-config.schema";
import { SSOProviderConfigFormField } from "./sso-provider-config-form-fields";
import { SsoConfigForms } from "./sso-provider-config-forms";
import { useT, tStatic } from "@seliseblocks/genesis-os";

const SSOGoogleFormFields = createCommonOAuthFields({
  clientId: {
    description: (
      <Link to="https://console.cloud.google.com/apis/credentials" target="_blank">
        <Button variant="link" className="h-fit !p-0" type="button">
          {tStatic("utilities.idp.authentication", "SSO_PROVIDER_CONFIG_GOOGLE_FORM.HOW_TO_OBTAIN_A_CLIENT_ID")}
        </Button>
      </Link>
    ),
  },
});

export const SSOProviderConfigGoogleForm: React.FC<SsoConfigForms> = ({ save, configuration }) => {
  const { t } = useT("utilities.idp.authentication");
  const values = configuration || {
    provider: SSO_PROVIDERS.google,
    audience: "",
    clientId: "",
    clientSecret: "",
    redirectUrl: "",
    initialRoles: [],
    initialPermissions: [],
    // Hardcoded default role for now; will use API response later
    userRoles: [{ name: "user", slug: "user", description: t("SSO_PROVIDER_CONFIG_GOOGLE_FORM.DEFAULT_ROLE"), itemId: "1234" }],
    userPermissions: [],
  };
  const form = useForm({
    values,
    resolver: zodResolver(createSsoOAuthProviderSchema()) as unknown as Resolver<typeof values>,
  });
  return (
    <Form {...form}>
      <form
        className="flex h-full flex-col justify-between gap-6"
        onSubmit={form.handleSubmit(save)}
      >
        <Card>
          <CardHeader>
            <CardTitle>{t("SSO_PROVIDER_CONFIG_GOOGLE_FORM.GENERAL")}</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <SSOProviderConfigFormField fields={SSOGoogleFormFields} form={form} />
          </CardContent>
        </Card>
        <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
          <FormField
            name="userRoles"
            control={form.control}
            render={({ field }) => (
              <SSOInitialRoles roles={form.getValues("userRoles")} onChange={field.onChange} />
            )}
          />
          <FormField
            name="userPermissions"
            control={form.control}
            render={({ field }) => (
              <SSOInitialPermissions
                permissions={form.getValues("userPermissions") || []}
                onChange={field.onChange}
              />
            )}
          />
        </div>

        <div className="flex items-center justify-end gap-2">
          <Button type="submit">{t("common:SAVE")}</Button>
        </div>
      </form>
    </Form>
  );
};
