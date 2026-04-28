import React from "react";
import { useForm } from "react-hook-form";

import { Form, FormField } from "@/components/ui-kits/form/form";
import { Button } from "@/components/ui-kits/button/button";
import { SSOProviderConfigFormField } from "./sso-provider-config-form-fields";
import { SsoConfigForms } from "./sso-provider-config-forms";
import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { SSOInitialRoles } from "@blocks-idp/authentication/components/sso-initial-roles";
import { SSOInitialPermissions } from "@blocks-idp/authentication/components/sso-initial-permissions";
import { zodResolver } from "@hookform/resolvers/zod";
import { createCommonOAuthFields } from "../sso-provider-config-field-factory.util";
import { ssoOAuthProviderSchema } from "../sso-provider-config.schema";

const SSOXFormFields = createCommonOAuthFields();

export const SSOProviderConfigXForm: React.FC<SsoConfigForms> = ({ save, configuration }) => {
  const form = useForm({
    values: configuration || {
      provider: SSO_PROVIDERS.x,
      audience: "",
      clientId: "",
      clientSecret: "",
      redirectUrl: "",
      initialRoles: [],
      initialPermissions: [],
      // Hardcoded default role for now; will use API response later
      userRoles: [{ name: "user", slug: "user", description: "default role", itemId: "1234" }],
      userPermissions: [],
    },
    resolver: zodResolver(ssoOAuthProviderSchema),
  });
  return (
    <Form {...form}>
      <form
        className="flex h-full flex-col justify-between gap-6"
        onSubmit={form.handleSubmit(save)}
      >
        <Card>
          <CardHeader>
            <CardTitle>General</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <SSOProviderConfigFormField fields={SSOXFormFields} form={form} />
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
          <Button type="submit">Save</Button>
        </div>
      </form>
    </Form>
  );
};
