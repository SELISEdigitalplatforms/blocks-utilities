import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Form } from "@/components/ui-kits/form/form";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useProjectStore } from "@/store/useProjectStore";
import {
  useSaveGetOIDCCredential,
  useSaveOIDCCredential,
} from "@blocks-idp/authentication/hooks/use-sso";
import { IGetOIDCCredentialResponse } from "@blocks-idp/authentication/models/sso.model";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { createCommonOAuthFields } from "../sso-provider-config-field-factory.util";
import { ssoProviderConfigBaseSchema } from "../sso-provider-config.schema";
import { SSOProviderConfigFormFieldType } from "../sso-provider-config.type";
import { SSOProviderConfigFormField } from "./sso-provider-config-form-fields";
import { SsoConfigForms } from "./sso-provider-config-forms";

const SCOPE_OPTIONS = [
  { label: "Open Id", value: "openid" },
  { label: "Email", value: "email" },
];

const SCOPE_VALUE_SET = new Set(SCOPE_OPTIONS.map((option) => option.value.toLowerCase()));

const toUniqueList = (values: string[]) => {
  const uniqueValues: string[] = [];

  values.forEach((value) => {
    const normalized = value.trim().toLowerCase();

    if (normalized && SCOPE_VALUE_SET.has(normalized) && !uniqueValues.includes(normalized)) {
      uniqueValues.push(normalized);
    }
  });

  return uniqueValues;
};

const DEFAULT_SCOPE_SELECTION = toUniqueList(SCOPE_OPTIONS.map((option) => option.value));

const parseScopeValue = (scope?: string | string[]) => {
  if (!scope) return DEFAULT_SCOPE_SELECTION;
  if (Array.isArray(scope)) return toUniqueList(scope);

  return toUniqueList(scope.split(/[\s,]+/));
};

const SSOBlocksFormFields: SSOProviderConfigFormFieldType[] = [
  ...createCommonOAuthFields({
    clientId: { description: "", isDisabled: true },
    clientSecret: {
      isDisabled: true,
    },
  }),
  {
    id: "6",
    label: "Scope",
    type: "multi-select",
    name: "scope",
    options: SCOPE_OPTIONS,
    placeholder: "Select scopes",
  },
  {
    id: "7",
    label: "Is Auto Redirect",
    type: "radio",
    name: "isAutoRedirect",
    options: [
      { label: "True", value: "true" },
      { label: "False", value: "false" },
    ],
  },
];

type FormValue = {
  provider: string;
  audience: string;
  clientId: string;
  clientSecret: string;
  redirectUrl: string;
  scope: string[];
  isAutoRedirect: "true" | "false";
};

const schema = ssoProviderConfigBaseSchema.extend({
  scope: z.array(z.string().trim()).nonempty({ message: "Select at least one scope." }),
  isAutoRedirect: z.enum(["true", "false"]),
});

export const SSOProviderConfigBlocksForm: React.FC<SsoConfigForms> = () => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  const { data: existingConfiguration } = useSaveGetOIDCCredential(tenantId);
  const { mutateAsync } = useSaveOIDCCredential();

  const mapResponseToFormValue = (configuration?: IGetOIDCCredentialResponse): FormValue => ({
    provider: "SELISE OIDC",
    audience: configuration?.audience || "",
    clientId: configuration?.itemId || "",
    clientSecret: configuration?.clientSecret || "",
    redirectUrl: configuration?.redirectUri || "",
    scope: parseScopeValue(configuration?.scope),
    isAutoRedirect: configuration?.isAutoRedirect ? "true" : "false",
  });

  const form = useForm<FormValue>({
    values: mapResponseToFormValue(existingConfiguration),
    resolver: zodResolver(schema),
  });

  const onFormSubmit = async (data: FormValue) => {
    const payload = {
      redirectUri: data.redirectUrl,
      audience: data.audience,
      scope: data.scope.join(" "),
      isAutoRedirect: data.isAutoRedirect === "true",
      itemId: existingConfiguration?.itemId ?? "",
      projectKey: tenantId,
    };
    const res = await mutateAsync(payload);

    if (!res.isSuccess) return showErrorToast({ errors: res.errors });
    showSuccessToast({ description: `Blocks OIDC is configured successfully` });
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
            <SSOProviderConfigFormField fields={SSOBlocksFormFields} form={form} />
          </CardContent>
        </Card>

        <div className="flex items-center justify-end gap-2">
          <Button type="submit">Save</Button>
        </div>
      </form>
    </Form>
  );
};
