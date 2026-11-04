import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_IDP_AUTHENTICATION_MODULE = "utilities.idp.authentication";

export const getUserCreationType = (): Record<number, string> => ({
  0: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.NONE"),
  1: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.PORTAL"),
  2: "API",
  3: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.SERVICE"),
  4: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.SOCIAL"),
});

export enum GRANT_TYPES {
  password = "password",
  social = "social",
  clientCredential = "client_credential",
  authorizationCode = "authorization_code",
}

export const getGrantTypesOptions = (): { id: GRANT_TYPES; label: string; value: string }[] => [
  { id: GRANT_TYPES.password, label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.EMAIL_PASSWORD"), value: GRANT_TYPES.password },
  { id: GRANT_TYPES.social, label: "SSO", value: GRANT_TYPES.social },
  {
    id: GRANT_TYPES.clientCredential,
    label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.CLIENT_CREDENTIAL"),
    value: GRANT_TYPES.clientCredential,
  },
  {
    id: GRANT_TYPES.authorizationCode,
    label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.AUTHORIZATION_CODE"),
    value: GRANT_TYPES.authorizationCode,
  },
];

export const getAuthenticationTabs = (): { id: string; label: string; value: string }[] => [
  // { id: "general", label: "General", value: "general" },
  { id: "users", label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.USERS"), value: "users" },
  { id: "organizations", label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.ORGANIZATIONS"), value: "organizations" },
  {
    id: GRANT_TYPES.clientCredential,
    label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.CLIENT_CREDENTIAL"),
    value: GRANT_TYPES.clientCredential,
  },
];

export const getSecretManagementTabs = (): { id: string; label: string; value: string }[] => [
  {
    id: "infra-config",
    label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.INFRA_CONFIG"),
    value: "infra-config",
  },
  {
    id: "my-secret",
    label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.MY_SECRET"),
    value: "my-secret",
  },
  {
    id: "managed-services",
    label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.MY_SERVICE"),
    value: "managed-services",
  },
  {
    id: GRANT_TYPES.authorizationCode,
    label: "OIDC",
    value: GRANT_TYPES.authorizationCode,
  },
  {
    id: GRANT_TYPES.social,
    label: "SSO",
    value: GRANT_TYPES.social,
  },
  {
    id: "external-idp",
    label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.EXTERNAL_IDP"),
    value: "external-idp",
  },
  {
    id: "captcha",
    label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.CAPTCHA"),
    value: "captcha",
  },
  {
    id: "mfa",
    label: "MFA",
    value: "mfa",
  },
  {
    id: "magic-url",
    label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.MAGIC_URL"),
    value: "magic-url",
  },
  {
    id: "storage",
    label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.STORAGE"),
    value: "storage",
  },
  {
    id: "ai-models",
    label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "AUTHENTICATION_CONSTANT.AI_MODELS"),
    value: "ai-models",
  },
];

export const providers = [
  { id: "keycloak", name: "Keycloak", icon: "/assets/images/keycloak_icon.png" },
  { id: "okta", name: "Okta", icon: "/assets/images/okta_symbol.png" },
  { id: "auth0", name: "Auth0", icon: "/assets/images/auth0.png" },
  { id: "azure", name: "Azure", icon: "/assets/images/azure.png" },
  { id: "others", name: "Others", icon: "" },
];
