import { API_BASES } from "@/constants/endpoint.constant";

// ─── Subpaths ─────────────────────────────────────────────────────────────────

const AUTH_SUBPATH = "/Authentication";

// ─── Auth endpoints (auth.service / oauth.service) ───────────────────────────

export const AUTH_ENDPOINTS = {
  TOKEN: `${API_BASES.IDP}${AUTH_SUBPATH}/Token`,
  LOGOUT: `${API_BASES.IDP}${AUTH_SUBPATH}/Logout`,
  GET_SOCIAL_LOGIN_ENDPOINT: `${API_BASES.IDP}${AUTH_SUBPATH}/GetSocialLogInEndPoint`,
  GET_LOGIN_OPTIONS: `${API_BASES.IDP}${AUTH_SUBPATH}/GetLoginOptions`,
} as const;

// ─── Client credential endpoints (auth-clients.service) ─────────────────────

export const AUTH_CLIENT_ENDPOINTS = {
  GET_CLIENT_CREDENTIALS: `${API_BASES.IDP}${AUTH_SUBPATH}/GetClientCredentials`,
  SAVE_CLIENT_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/SaveClientCredential`,
  DELETE_CLIENT_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/DeleteClientCredential`,
} as const;

// ─── OIDC client endpoints (auth-clients-oidc.service) ──────────────────────

export const AUTH_OIDC_ENDPOINTS = {
  GET_OIDC_CLIENTS: `${API_BASES.IDP}${AUTH_SUBPATH}/GetOIDCClients`,
  GET_OIDC_CLIENT: `${API_BASES.IDP}${AUTH_SUBPATH}/GetOIDCClient`,
  SAVE_OIDC_CLIENT: `${API_BASES.IDP}${AUTH_SUBPATH}/SaveOIDCClient`,
  DELETE_OIDC_CLIENT: `${API_BASES.IDP}${AUTH_SUBPATH}/DeleteOIDCClient`,
} as const;

// ─── Auth configuration endpoints (auth-config.service) ─────────────────────

export const AUTH_CONFIG_ENDPOINTS = {
  GET_CONFIG: `${API_BASES.CLOUD_CONFIGURATION}${AUTH_SUBPATH}/Get`,
  UPDATE_CONFIG: `${API_BASES.CLOUD_CONFIGURATION}${AUTH_SUBPATH}/Update`,
} as const;

// ─── SSO endpoints (social.service) ─────────────────────────────────────────

export const SSO_ENDPOINTS = {
  GET_SSO_CREDENTIALS: `${API_BASES.IDP}${AUTH_SUBPATH}/GetSsoCredentials`,
  GET_SSO_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/GetSsoCredential`,
  SAVE_SSO_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/SaveSsoCredential`,
  DELETE_SSO_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/DeleteSsoCredential`,
  UPDATE_STATUS: `${API_BASES.IDP}${AUTH_SUBPATH}/UpdateStatus`,
} as const;

// ─── OIDC flow endpoints (oidc-auth-flow.service) ───────────────────────────

export const OIDC_FLOW_ENDPOINTS = {
  USER_ACKNOWLEDGEMENT: `${API_BASES.IDP}${AUTH_SUBPATH}/UserAcknowledgement`,
} as const;

// ─── Legacy re-export (backward compat for oauth.service) ───────────────────

export const IDP_ENDPOINTS = {
  AUTHENTICATION: {
    GET_SOCIAL_LOGIN_ENDPOINT: AUTH_ENDPOINTS.GET_SOCIAL_LOGIN_ENDPOINT,
    TOKEN: AUTH_ENDPOINTS.TOKEN,
  },
};
