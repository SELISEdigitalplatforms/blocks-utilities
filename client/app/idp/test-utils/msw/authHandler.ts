import { http, HttpResponse, type JsonBodyType } from "msw";
import {
  mockSigninResponse,
  mockSignupResponse,
  mockClientCredentialsResponse,
  mockOidcCredentialsResponse,
  mockOidcCredentialResponse,
  mockGetAuthConfigResponse,
  mockSsoCredentialsResponse,
  mockSsoCredential,
  mockGetSocialLoginResponse,
  mockSigninBySSOResponse,
} from "../__mocks__/auth.data.mock";
import { mockSuccessResponse, mockSuccessResponseWithItemId } from "@/test-utils/__mocks__";
import {
  AUTH_ENDPOINTS,
  AUTH_CLIENT_ENDPOINTS,
  AUTH_OIDC_ENDPOINTS,
  AUTH_CONFIG_ENDPOINTS,
  SSO_ENDPOINTS,
  OIDC_FLOW_ENDPOINTS,
} from "../../authentication/constants/endpoint.constant";
import { PEOPLE_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";

// ─── Endpoint Patterns ────────────────────────────────────────────────────────

// Auth
const TOKEN_PATTERN = new RegExp(AUTH_ENDPOINTS.TOKEN);
const LOGOUT_PATTERN = new RegExp(AUTH_ENDPOINTS.LOGOUT);
const GET_SOCIAL_LOGIN_PATTERN = new RegExp(AUTH_ENDPOINTS.GET_SOCIAL_LOGIN_ENDPOINT);
const SIGNUP_PATTERN = new RegExp(PEOPLE_ENDPOINTS.SIGNUP);

// Client Credentials
const GET_CLIENT_CREDENTIALS_PATTERN = new RegExp(AUTH_CLIENT_ENDPOINTS.GET_CLIENT_CREDENTIALS);
const SAVE_CLIENT_CREDENTIAL_PATTERN = new RegExp(AUTH_CLIENT_ENDPOINTS.SAVE_CLIENT_CREDENTIAL);
const DELETE_CLIENT_CREDENTIAL_PATTERN = new RegExp(AUTH_CLIENT_ENDPOINTS.DELETE_CLIENT_CREDENTIAL);

// OIDC
const GET_OIDC_CLIENTS_PATTERN = new RegExp(AUTH_OIDC_ENDPOINTS.GET_OIDC_CLIENTS);
const GET_OIDC_CLIENT_PATTERN = new RegExp(`${AUTH_OIDC_ENDPOINTS.GET_OIDC_CLIENT}\\?`);
const SAVE_OIDC_CLIENT_PATTERN = new RegExp(AUTH_OIDC_ENDPOINTS.SAVE_OIDC_CLIENT);
const DELETE_OIDC_CLIENT_PATTERN = new RegExp(AUTH_OIDC_ENDPOINTS.DELETE_OIDC_CLIENT);

// Auth Config
const GET_AUTH_CONFIG_PATTERN = new RegExp(`${AUTH_CONFIG_ENDPOINTS.GET_CONFIG}\\?`);
const UPDATE_AUTH_CONFIG_PATTERN = new RegExp(AUTH_CONFIG_ENDPOINTS.UPDATE_CONFIG);

// SSO
const GET_SSO_CREDENTIALS_PATTERN = new RegExp(`${SSO_ENDPOINTS.GET_SSO_CREDENTIALS}\\?`);
const GET_SSO_CREDENTIAL_PATTERN = new RegExp(`${SSO_ENDPOINTS.GET_SSO_CREDENTIAL}\\?`);
const SAVE_SSO_CREDENTIAL_PATTERN = new RegExp(SSO_ENDPOINTS.SAVE_SSO_CREDENTIAL);
const DELETE_SSO_CREDENTIAL_PATTERN = new RegExp(SSO_ENDPOINTS.DELETE_SSO_CREDENTIAL);
const UPDATE_SSO_STATUS_PATTERN = new RegExp(SSO_ENDPOINTS.UPDATE_STATUS);

// OIDC Flow
const USER_ACKNOWLEDGEMENT_PATTERN = new RegExp(OIDC_FLOW_ENDPOINTS.USER_ACKNOWLEDGEMENT);

// ─── Default Handlers (happy-path) ───────────────────────────────────────────

export const authHandlers = [
  // Auth
  http.post(TOKEN_PATTERN, () => HttpResponse.json(mockSigninResponse)),
  http.post(LOGOUT_PATTERN, () => HttpResponse.json(mockSuccessResponse)),
  http.post(GET_SOCIAL_LOGIN_PATTERN, () => HttpResponse.json(mockGetSocialLoginResponse)),
  http.post(SIGNUP_PATTERN, () => HttpResponse.json(mockSignupResponse)),

  // Client Credentials
  http.get(GET_CLIENT_CREDENTIALS_PATTERN, () => HttpResponse.json(mockClientCredentialsResponse)),
  http.post(SAVE_CLIENT_CREDENTIAL_PATTERN, () => HttpResponse.json(mockSuccessResponseWithItemId)),
  http.post(DELETE_CLIENT_CREDENTIAL_PATTERN, () => HttpResponse.json(mockSuccessResponse)),

  // OIDC
  http.get(GET_OIDC_CLIENTS_PATTERN, () => HttpResponse.json(mockOidcCredentialsResponse)),
  http.get(GET_OIDC_CLIENT_PATTERN, () => HttpResponse.json(mockOidcCredentialResponse)),
  http.post(SAVE_OIDC_CLIENT_PATTERN, () => HttpResponse.json(mockSuccessResponseWithItemId)),
  http.post(DELETE_OIDC_CLIENT_PATTERN, () => HttpResponse.json(mockSuccessResponse)),

  // Auth Config
  http.get(GET_AUTH_CONFIG_PATTERN, () => HttpResponse.json(mockGetAuthConfigResponse)),
  http.post(UPDATE_AUTH_CONFIG_PATTERN, () => HttpResponse.json(mockSuccessResponse)),

  // SSO
  http.get(GET_SSO_CREDENTIALS_PATTERN, () => HttpResponse.json(mockSsoCredentialsResponse)),
  http.get(GET_SSO_CREDENTIAL_PATTERN, () => HttpResponse.json(mockSsoCredential)),
  http.post(SAVE_SSO_CREDENTIAL_PATTERN, () => HttpResponse.json(mockSuccessResponseWithItemId)),
  http.post(DELETE_SSO_CREDENTIAL_PATTERN, () => HttpResponse.json(mockSuccessResponse)),
  http.post(UPDATE_SSO_STATUS_PATTERN, () => HttpResponse.json(mockSuccessResponse)),

  // OIDC Flow
  http.post(USER_ACKNOWLEDGEMENT_PATTERN, () =>
    HttpResponse.json({ redirectUrl: "https://app.blocks.com/callback?code=abc123" }),
  ),
];

// ─── Per-Test Override Factories ──────────────────────────────────────────────

// Auth
export const signinHandler = (response: JsonBodyType = mockSigninResponse) =>
  http.post(TOKEN_PATTERN, () => HttpResponse.json(response));

export const signinErrorHandler = (status = 500) =>
  http.post(TOKEN_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const signupHandler = (response: JsonBodyType = mockSignupResponse) =>
  http.post(SIGNUP_PATTERN, () => HttpResponse.json(response));

export const signupErrorHandler = (status = 500) =>
  http.post(SIGNUP_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const getSocialLoginHandler = (response: JsonBodyType = mockGetSocialLoginResponse) =>
  http.post(GET_SOCIAL_LOGIN_PATTERN, () => HttpResponse.json(response));

export const signinBySSOHandler = (response: JsonBodyType = mockSigninBySSOResponse) =>
  http.post(TOKEN_PATTERN, () => HttpResponse.json(response));

// Client Credentials
export const getClientCredentialsHandler = (
  response: JsonBodyType = mockClientCredentialsResponse,
) => http.get(GET_CLIENT_CREDENTIALS_PATTERN, () => HttpResponse.json(response));

export const getClientCredentialsErrorHandler = (status = 500) =>
  http.get(GET_CLIENT_CREDENTIALS_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const saveClientCredentialHandler = (
  response: JsonBodyType = mockSuccessResponseWithItemId,
) => http.post(SAVE_CLIENT_CREDENTIAL_PATTERN, () => HttpResponse.json(response));

export const deleteClientCredentialHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(DELETE_CLIENT_CREDENTIAL_PATTERN, () => HttpResponse.json(response));

// OIDC
export const getOidcClientsHandler = (response: JsonBodyType = mockOidcCredentialsResponse) =>
  http.get(GET_OIDC_CLIENTS_PATTERN, () => HttpResponse.json(response));

export const getOidcClientsErrorHandler = (status = 500) =>
  http.get(GET_OIDC_CLIENTS_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const getOidcClientHandler = (response: JsonBodyType = mockOidcCredentialResponse) =>
  http.get(GET_OIDC_CLIENT_PATTERN, () => HttpResponse.json(response));

export const saveOidcClientHandler = (response: JsonBodyType = mockSuccessResponseWithItemId) =>
  http.post(SAVE_OIDC_CLIENT_PATTERN, () => HttpResponse.json(response));

export const deleteOidcClientHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(DELETE_OIDC_CLIENT_PATTERN, () => HttpResponse.json(response));

// Auth Config
export const getAuthConfigHandler = (response: JsonBodyType = mockGetAuthConfigResponse) =>
  http.get(GET_AUTH_CONFIG_PATTERN, () => HttpResponse.json(response));

export const getAuthConfigErrorHandler = (status = 500) =>
  http.get(GET_AUTH_CONFIG_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const updateAuthConfigHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(UPDATE_AUTH_CONFIG_PATTERN, () => HttpResponse.json(response));

// SSO
export const getSsoCredentialsHandler = (response: JsonBodyType = mockSsoCredentialsResponse) =>
  http.get(GET_SSO_CREDENTIALS_PATTERN, () => HttpResponse.json(response));

export const getSsoCredentialsErrorHandler = (status = 500) =>
  http.get(GET_SSO_CREDENTIALS_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const getSsoCredentialHandler = (response: JsonBodyType = mockSsoCredential) =>
  http.get(GET_SSO_CREDENTIAL_PATTERN, () => HttpResponse.json(response));

export const saveSsoCredentialHandler = (response: JsonBodyType = mockSuccessResponseWithItemId) =>
  http.post(SAVE_SSO_CREDENTIAL_PATTERN, () => HttpResponse.json(response));

export const deleteSsoCredentialHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(DELETE_SSO_CREDENTIAL_PATTERN, () => HttpResponse.json(response));

export const updateSsoStatusHandler = (response: JsonBodyType = mockSuccessResponse) =>
  http.post(UPDATE_SSO_STATUS_PATTERN, () => HttpResponse.json(response));

// OIDC Flow
export const userAcknowledgementHandler = (
  response: JsonBodyType = { redirectUrl: "https://app.blocks.com/callback?code=abc123" },
) => http.post(USER_ACKNOWLEDGEMENT_PATTERN, () => HttpResponse.json(response));

export const userAcknowledgementErrorHandler = (status = 500) =>
  http.post(USER_ACKNOWLEDGEMENT_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );
