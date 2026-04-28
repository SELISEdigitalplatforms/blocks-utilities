import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Form } from "@/components/ui-kits/form/form";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useProjectStore } from "@/store/useProjectStore";
import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";
import { useSaveSsoCredential } from "@blocks-idp/authentication/hooks/use-sso";
import { ISsoProviderConfiguration } from "@blocks-idp/authentication/models/sso.model";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { createCommonOAuthFields } from "../sso-provider-config-field-factory.util";
import { ssoProviderConfigBaseSchema } from "../sso-provider-config.schema";
import { SSOProviderConfigFormFieldType } from "../sso-provider-config.type";
import { SSOProviderConfigFormField } from "./sso-provider-config-form-fields";
import { SsoConfigForms } from "./sso-provider-config-forms";

const SSOOwnSSOFormFields: SSOProviderConfigFormFieldType[] = [
  ...createCommonOAuthFields({ clientId: { description: "" } }),
  {
    id: "6",
    label: "Well Known URL",
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

const schema = ssoProviderConfigBaseSchema.extend({
  clientId: z.string().trim().nonempty("Client id is required"),
  clientSecret: z.string().trim().nonempty("Client secret is required"),
  wellKnownUrl: z.string().url({ message: "Well known URL must be a valid URL." }).trim(),
});

export const SSOProviderConfigOwnSSOForm: React.FC<SsoConfigForms> = ({ configuration }) => {
  const projectKey = useProjectStore()?.selectedProject?.tenantId || "";
  const { mutateAsync } = useSaveSsoCredential();

  const form = useForm({
    values: configuration || {
      provider: SSO_PROVIDERS.ownsso,
      audience: "",
      clientId: "",
      clientSecret: "",
      redirectUrl: "",
      wellKnownUrl: "",
    },
    resolver: zodResolver(schema),
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
    showSuccessToast({ description: `Bring your own SSO is configured successfully` });
  };

  return (
    <Form {...form}>
      <form
        className="flex h-full flex-col justify-between gap-6"
        onSubmit={form.handleSubmit(onFormSubmit)}
      >
        <Card>
          <CardHeader>
            <CardTitle>General</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <SSOProviderConfigFormField fields={SSOOwnSSOFormFields} form={form} />
          </CardContent>
        </Card>

        <div className="flex items-center justify-end gap-2">
          <Button type="submit">Save</Button>
        </div>
      </form>
    </Form>
  );
};
