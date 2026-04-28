const Google = "/assets/images/social-media-google.png";
const Microsoft = "/assets/images/social-media-ms.png";
const GithubDark = "/assets/images/github-dark-mode.png";
const Github = "/assets/images/social-media-github.png";
const LinkedIn = "/assets/images/social-media-in.png";
const AppleDark = "/assets/images/apple-dark-mode-logo.png";
const Apple = "/assets/images/social-media-apple.png";
const Facebook = "/assets/images/social-media-facebook.png";
const Selise = "/assets/images/selise-globe-logo.png";
const XDark = "/assets/images/twitter-x-dark-mode-logo.png";
const X = "/assets/images/twitter-x-light-mode-logo.png";
import {
  ISsoProviderConfigurationWithMeta,
  ISsoProviderFrontendMeta,
} from "@blocks-idp/authentication/models/sso.model";

export enum SSO_PROVIDERS {
  google = "google",
  microsoft = "microsoft",
  github = "github",
  linkedin = "linkedin",
  x = "x",
  apple = "apple",
  facebook = "facebook",
  ownsso = "ownsso",
}

/** Shared empty defaults for all provider configuration fields. */
const PROVIDER_DEFAULTS: Omit<
  ISsoProviderConfigurationWithMeta,
  keyof ISsoProviderFrontendMeta | "provider"
> = {
  itemId: "",
  createdDate: "",
  lastUpdatedDate: "",
  createdBy: "",
  lastUpdatedBy: "",
  language: "en",
  organizationIds: [],
  tags: [],
  audience: "",
  clientId: "",
  clientSecret: "",
  authorizationUrl: "",
  tokenUrl: "",
  getProfileUrl: "",
  redirectUrl: "",
  scope: [],
  initialRoles: [],
  initialPermissions: [],
  isDisabled: false,
  userRoles: [],
  userPermissions: [],
};

/**
 * Creates a fully-typed provider config by merging shared defaults with
 * provider-specific metadata. Eliminates the ~20-field boilerplate per entry.
 */
function createProviderConfig(
  provider: SSO_PROVIDERS,
  meta: ISsoProviderFrontendMeta,
): ISsoProviderConfigurationWithMeta {
  return { ...PROVIDER_DEFAULTS, provider, ...meta };
}

export const SOCIAL_AUTH_PROVIDERS_CONFIG: Record<
  SSO_PROVIDERS,
  ISsoProviderConfigurationWithMeta
> = {
  github: createProviderConfig(SSO_PROVIDERS.github, {
    label: "GitHub",
    description: "Enable the GitHub login option for your Auth0 applications",
    imageSrc: Github,
    imageSrcDark: GithubDark,
    isAvailable: true,
    isConfigured: false,
  }),
  google: createProviderConfig(SSO_PROVIDERS.google, {
    label: "Google",
    description: "Allow your users to seamlessly log in with their trusted Google Account.",
    imageSrc: Google,
    isAvailable: true,
    isConfigured: false,
  }),
  microsoft: createProviderConfig(SSO_PROVIDERS.microsoft, {
    label: "Microsoft",
    description: "Enable your users to securely sign in through their trusted Microsoft Account.",
    imageSrc: Microsoft,
    isAvailable: true,
    isConfigured: false,
  }),
  linkedin: createProviderConfig(SSO_PROVIDERS.linkedin, {
    label: "LinkedIn",
    description:
      "Leverage the largest professional social network to enhance your sign-in experience",
    imageSrc: LinkedIn,
    isAvailable: true,
    isConfigured: false,
  }),
  x: createProviderConfig(SSO_PROVIDERS.x, {
    label: "X",
    description: "Twitter allows users to enjoy the benefits of login with as little as one...",
    imageSrc: X,
    imageSrcDark: XDark,
    isAvailable: true,
    isConfigured: false,
  }),
  apple: createProviderConfig(SSO_PROVIDERS.apple, {
    label: "Apple",
    description: "The easy way to add Sign in with Apple to your app or website",
    imageSrc: Apple,
    imageSrcDark: AppleDark,
    isAvailable: true,
    isConfigured: false,
  }),
  facebook: createProviderConfig(SSO_PROVIDERS.facebook, {
    label: "Facebook",
    description: "A fast and convenient way for users to log into your app with Facebook",
    imageSrc: Facebook,
    isAvailable: false,
    isConfigured: false,
  }),
  ownsso: createProviderConfig(SSO_PROVIDERS.ownsso, {
    label: "Bring your own SSO",
    description: "Bring your own SSO provider",
    imageSrc: Selise,
    isAvailable: true,
    isConfigured: false,
  }),
};
