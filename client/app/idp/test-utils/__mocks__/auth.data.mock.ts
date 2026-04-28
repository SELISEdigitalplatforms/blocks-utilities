import { TEST_PROJECT_KEY, mockSuccessResponse, mockErrorResponse } from "@/test-utils/__mocks__";
import type { ISigninByEmailPayload } from "../../authentication/models/auth.model";
import type {
  ISignupByEmailPayload,
  ISignupByEmailResponse,
} from "../../authentication/models/auth.model";
import type { IVerifyMfaPayload, IVerifyMfaResponse } from "../../authentication/models/auth.model";
import type {
  IGetSocialLoginEndpointPayload,
  ISigninBySSOPayload,
} from "../../authentication/models/oauth.model";
import type {
  IAuthConfigPayload,
  ISaveAuthConfigPayload,
  IAuthConfiguration,
} from "../../authentication/models/auth-configuration.model";
import type {
  IGetClientsPayload,
  IClientConfigResponse,
  ISaveClientCredentialPayload,
  IDeleteOidcClientPayload,
  IOidcConfigResponse,
  IGetOidcPayload,
  ISaveOidcCredentialPayload,
} from "../../authentication/models/auth.oidc.model";
import type { JwtClaimPayload } from "../../authentication/models/jwt.claim.model";
import type {
  ISaveSsoCredentialPayload,
  ISsoProviderConfiguration,
  IGetSsoCredentialsPayload,
  IGetSsoCredentialByIdPayload,
  IDeleteSsoCredentialPayload,
  IUpdateSsoCredentialStatusPayload,
} from "../../authentication/models/sso.model";

export { mockSuccessResponse, mockErrorResponse };

// ─── Mock IDs ─────────────────────────────────────────────────────────────────

export const MOCK_CLIENT_ITEM_ID = "client-a1b2-c3d4";
export const MOCK_OIDC_ITEM_ID = "oidc-e5f6-g7h8";
export const MOCK_SSO_ITEM_ID = "sso-i9j0-k1l2";
export const MOCK_USER_ID = "user-m3n4-o5p6";

// ─── Auth Mocks ───────────────────────────────────────────────────────────────

export const mockSigninPayload: ISigninByEmailPayload = {
  username: "test@blocks.com",
  password: "Test@1234",
};

export const mockSigninResponse = {
  access_token: "mock-access-token",
  token_type: "Bearer",
  expires_in: 3600,
  refresh_token: "mock-refresh-token",
};

export const mockSigninMfaResponse = {
  enable_mfa: true,
  message: "MFA required",
  mfaType: 1,
  mfaId: "mfa-id-123",
};

export const mockSignupPayload: ISignupByEmailPayload = {
  email: "newuser@blocks.com",
  captchaCode: "captcha-code",
};

export const mockSignupResponse: ISignupByEmailResponse = {
  itemId: MOCK_USER_ID,
  errors: null,
  isSuccess: true,
};

export const mockVerifyMfaPayload: IVerifyMfaPayload = {
  code: "123456",
  mfa_id: "mfa-id-123",
  mfa_type: 1,
};

export const mockVerifyMfaResponse: IVerifyMfaResponse = {
  access_token: "mock-access-token-after-mfa",
  token_type: "Bearer",
  expires_in: 3600,
  refresh_token: "mock-refresh-token-after-mfa",
};

// ─── OAuth / SSO Mocks ───────────────────────────────────────────────────────

export const mockGetSocialLoginPayload: IGetSocialLoginEndpointPayload = {
  provider: "google" as never,
  audience: "blocks-cloud",
  sendAsResponse: false,
};

export const mockGetSocialLoginResponse = {
  error: null,
  isAResponse: false,
  providerUrl: "https://accounts.google.com/o/oauth2/v2/auth?client_id=123",
};

export const mockSigninBySSOPayload: ISigninBySSOPayload = {
  code: "sso-auth-code",
  state: "sso-state-token",
};

export const mockSigninBySSOResponse = {
  access_token: "mock-sso-access-token",
  expires_in: 3600,
  refresh_token: "mock-sso-refresh-token",
  token_type: "Bearer",
  enable_mfa: false,
  message: "",
  mfaId: "",
  mfaType: "",
};

// ─── Client Credentials Mocks ────────────────────────────────────────────────

export const mockGetClientsPayload: IGetClientsPayload = {
  projectKey: TEST_PROJECT_KEY,
};

export const mockClientCredential: IClientConfigResponse = {
  scope: "api",
  itemId: MOCK_CLIENT_ITEM_ID,
  name: "Test Client",
  createdDate: "2026-01-15T10:00:00Z",
  lastUpdatedDate: "2026-01-15T10:00:00Z",
  createdBy: "admin",
  language: "en",
  lastUpdatedBy: "admin",
  organizationIds: [],
  tags: [],
  clientSecret: "mock-client-secret",
  roles: ["admin"],
  isActive: true,
  audiences: ["blocks-cloud"],
};

export const mockClientCredentialsResponse = [mockClientCredential];

export const mockSaveClientPayload: ISaveClientCredentialPayload = {
  name: "New Client",
  roles: ["admin"],
  projectKey: TEST_PROJECT_KEY,
};

export const mockDeleteClientPayload: IDeleteOidcClientPayload = {
  itemId: MOCK_CLIENT_ITEM_ID,
  projectKey: TEST_PROJECT_KEY,
};

// ─── OIDC Mocks ──────────────────────────────────────────────────────────────

export const mockGetOidcPayload: IGetOidcPayload = {
  projectKey: TEST_PROJECT_KEY,
};

export const mockOidcCredential: IOidcConfigResponse = {
  itemId: MOCK_OIDC_ITEM_ID,
  createdDate: "2026-01-15T10:00:00Z",
  lastUpdatedDate: "2026-01-15T10:00:00Z",
  createdBy: "admin",
  language: "en",
  lastUpdatedBy: "admin",
  organizationIds: [],
  tags: [],
  clientSecret: "mock-oidc-secret",
  redirectUri: "https://app.blocks.com/callback",
  scope: "openid profile email",
  audience: "blocks-cloud",
  isAutoRedirect: false,
  tenantId: "tenant-123",
  clientDisplayName: "Test OIDC App",
};

export const mockOidcCredentialsResponse = {
  oIDCClientCredentials: [mockOidcCredential],
  errors: null,
  isSuccess: true,
};

export const mockOidcCredentialResponse = {
  oIDCClientCredential: mockOidcCredential,
  errors: null,
  isSuccess: true,
};

export const mockSaveOidcPayload: ISaveOidcCredentialPayload = {
  audience: "blocks-cloud",
  isAutoRedirect: false,
  itemId: MOCK_OIDC_ITEM_ID,
  projectKey: TEST_PROJECT_KEY,
  redirectUri: "https://app.blocks.com/callback",
  scope: "openid profile email",
  clientDisplayName: "Test OIDC App",
};

// ─── Auth Config Mocks ───────────────────────────────────────────────────────

export const mockGetAuthConfigPayload: IAuthConfigPayload = {
  projectKey: TEST_PROJECT_KEY,
};

export const mockAuthConfiguration: IAuthConfiguration = {
  accessTokenValidForNumberMinutes: 60,
  accountLockDurationInMinutes: 30,
  allowedGrantTypes: ["password", "client_credentials"],
  getNumberOfWrongAttemptsToLockTheAccount: 5,
  itemId: "auth-config-001",
  refreshTokenValidForNumberMinutes: 1440,
  rememberMeRefreshTokenValidForNumberMinutes: 43200,
  publicCertificatePath: "",
  isSelfSignUpAllowed: true,
};

export const mockGetAuthConfigResponse = {
  ...mockAuthConfiguration,
  errors: null,
  isSuccess: true,
};

export const mockSaveAuthConfigPayload: ISaveAuthConfigPayload = {
  itemId: "auth-config-001",
  refreshTokenValidForNumberMinutes: 1440,
  getNumberOfWrongAttemptsToLockTheAccount: 5,
  accountLockDurationInMinutes: 30,
  accessTokenValidForNumberMinutes: 60,
  rememberMeRefreshTokenValidForNumberMinutes: 43200,
  allowedGrantTypes: ["password", "client_credentials"],
  projectKey: TEST_PROJECT_KEY,
  isSelfSignUpAllowed: true,
};

// ─── SSO Provider Mocks ─────────────────────────────────────────────────────

export const mockGetSsoCredentialsPayload: IGetSsoCredentialsPayload = {
  projectKey: TEST_PROJECT_KEY,
};

export const mockSsoCredential: ISsoProviderConfiguration = {
  itemId: MOCK_SSO_ITEM_ID,
  createdDate: "2026-01-15T10:00:00Z",
  lastUpdatedDate: "2026-01-15T10:00:00Z",
  createdBy: "admin",
  language: "en",
  lastUpdatedBy: "admin",
  organizationIds: [],
  tags: [],
  provider: "google" as never,
  audience: "blocks-cloud",
  clientId: "sso-client-id",
  clientSecret: "sso-client-secret",
  authorizationUrl: "https://accounts.google.com/o/oauth2/v2/auth",
  tokenUrl: "https://oauth2.googleapis.com/token",
  getProfileUrl: "https://www.googleapis.com/oauth2/v2/userinfo",
  redirectUrl: "https://app.blocks.com/sso/callback",
  scope: ["openid", "profile", "email"],
  initialRoles: ["user"],
  initialPermissions: [],
  isDisabled: false,
  userRoles: [],
  userPermissions: [],
};

export const mockSsoCredentialsResponse = [mockSsoCredential];

export const mockGetSsoCredentialByIdPayload: IGetSsoCredentialByIdPayload = {
  itemId: MOCK_SSO_ITEM_ID,
  projectKey: TEST_PROJECT_KEY,
};

export const mockSaveSsoPayload: ISaveSsoCredentialPayload = {
  provider: "google",
  audience: "blocks-cloud",
  clientId: "sso-client-id",
  clientSecret: "sso-client-secret",
  redirectUrl: "https://app.blocks.com/sso/callback",
  projectKey: TEST_PROJECT_KEY,
  initialRoles: ["user"],
  initialPermissions: [],
};

export const mockDeleteSsoPayload: IDeleteSsoCredentialPayload = {
  itemId: MOCK_SSO_ITEM_ID,
  projectKey: TEST_PROJECT_KEY,
};

export const mockUpdateSsoStatusPayload: IUpdateSsoCredentialStatusPayload = {
  itemId: MOCK_SSO_ITEM_ID,
  isEnabled: true,
  projectKey: TEST_PROJECT_KEY,
};

// ─── JWT Claim Mocks ─────────────────────────────────────────────────────────

export const mockJwtClaimPayload: JwtClaimPayload = {
  userId: MOCK_USER_ID,
  email: "test@blocks.com",
  name: "Test User",
  userName: "testuser",
  roles: "admin",
  projectKey: TEST_PROJECT_KEY,
};
