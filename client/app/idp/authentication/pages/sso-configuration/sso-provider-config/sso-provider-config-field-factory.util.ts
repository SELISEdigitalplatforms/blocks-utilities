import { ReactNode } from "react";
import type { SSOProviderConfigFormFieldType } from "./sso-provider-config.type";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_IDP_AUTHENTICATION_MODULE = "utilities.idp.authentication";

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
  createProviderField("1", tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "common:NAME"), "provider", "input", {
    isDisabled: true,
    description:
      tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "SSO_PROVIDER_CONFIG_FIELD_FACTORY_UTIL.IF_YOU_ARE_TRIGGERING_A"),
    ...overrides,
  });

export const createClientIdField = (overrides?: FieldOverrides): SSOProviderConfigFormFieldType =>
  createProviderField("2", tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "SSO_PROVIDER_CONFIG_FIELD_FACTORY_UTIL.CLIENT_ID"), "clientId", "input", overrides);

export const createClientSecretField = (
  overrides?: FieldOverrides,
): SSOProviderConfigFormFieldType =>
  createProviderField("3", tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "SSO_PROVIDER_CONFIG_FIELD_FACTORY_UTIL.CLIENT_SECRET"), "clientSecret", "password", {
    description: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "SSO_PROVIDER_CONFIG_FIELD_FACTORY_UTIL.FOR_SECURITY_PURPOSES_WE_DON"),
    ...overrides,
  });

export const createRedirectUrlField = (
  overrides?: FieldOverrides,
): SSOProviderConfigFormFieldType =>
  createProviderField("4", tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "SSO_PROVIDER_CONFIG_FIELD_FACTORY_UTIL.REDIRECT_URL"), "redirectUrl", "input", overrides);

export const createAudienceField = (overrides?: FieldOverrides): SSOProviderConfigFormFieldType =>
  createProviderField("5", tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "SSO_PROVIDER_CONFIG_FIELD_FACTORY_UTIL.AUDIENCE"), "audience", "input", overrides);

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
