import { ReactNode } from "react";
import type { SSOProviderConfigFormFieldType } from "./sso-provider-config.type";

type FieldOverrides = {
  description?: ReactNode;
  isDisabled?: boolean;
};

export const createProviderField = (
  id: string,
  label: string,
  name: string,
  type: SSOProviderConfigFormFieldType["type"],
  overrides?: FieldOverrides,
): SSOProviderConfigFormFieldType => {
  const base = { id, label, name, ...overrides };

  switch (type) {
    case "password":
      return { ...base, type };
    default:
      return { ...base, type: "input" };
  }
};

export const createNameField = (overrides?: FieldOverrides): SSOProviderConfigFormFieldType =>
  createProviderField("1", "Name", "provider", "input", {
    isDisabled: true,
    description:
      "If you are triggering a login manually, this is the identifier you would use on the connection parameter.",
    ...overrides,
  });

export const createClientIdField = (overrides?: FieldOverrides): SSOProviderConfigFormFieldType =>
  createProviderField("2", "Client ID", "clientId", "input", overrides);

export const createClientSecretField = (
  overrides?: FieldOverrides,
): SSOProviderConfigFormFieldType =>
  createProviderField("3", "Client Secret", "clientSecret", "password", {
    description: "For security purposes, we don't show your existing client secret.",
    ...overrides,
  });

export const createRedirectUrlField = (
  overrides?: FieldOverrides,
): SSOProviderConfigFormFieldType =>
  createProviderField("4", "Redirect Url", "redirectUrl", "input", overrides);

export const createAudienceField = (overrides?: FieldOverrides): SSOProviderConfigFormFieldType =>
  createProviderField("5", "Audience", "audience", "input", overrides);

export const createCommonOAuthFields = (overrides?: {
  name?: FieldOverrides;
  clientId?: FieldOverrides;
  clientSecret?: FieldOverrides;
  redirectUrl?: FieldOverrides;
  audience?: FieldOverrides;
}): SSOProviderConfigFormFieldType[] => [
  createNameField(overrides?.name),
  createClientIdField(overrides?.clientId),
  createClientSecretField(overrides?.clientSecret),
  createRedirectUrlField(overrides?.redirectUrl),
  createAudienceField(overrides?.audience),
];
