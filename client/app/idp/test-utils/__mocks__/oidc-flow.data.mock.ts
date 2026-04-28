import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__";

// ─── OIDC Auth Flow Mocks ────────────────────────────────────────────────────
// Mocks for the standalone OIDC auth-flow functions that use custom fetch()
// rather than the shared http client.

export const MOCK_OIDC_CLIENT_ID = "oidc-flow-client-123";
export const MOCK_OIDC_STATE = "oidc-state-abc";
export const MOCK_OIDC_NONCE = "oidc-nonce-def";

export const mockOidcFlowCredentialPayload = {
  projectKey: TEST_PROJECT_KEY,
  clientId: MOCK_OIDC_CLIENT_ID,
};

export const mockOidcFlowCredentialResponse = {
  oIDCClientCredential: {
    redirectUri: "https://app.blocks.com/callback",
    scope: "openid profile email",
    logoUrl: "https://cdn.blocks.com/logo.png",
    themeColor: "#124091",
    state: MOCK_OIDC_STATE,
    clientId: MOCK_OIDC_CLIENT_ID,
  },
  errors: null,
  isSuccess: true,
};

export const mockUserAcknowledgementPayload = {
  clientId: MOCK_OIDC_CLIENT_ID,
  state: MOCK_OIDC_STATE,
  nonce: MOCK_OIDC_NONCE,
  scope: "openid profile email",
  redirectUri: "https://app.blocks.com/callback",
  isAcknowledged: true,
  username: "testuser",
  projectKey: TEST_PROJECT_KEY,
};

export const mockUserAcknowledgementResponse = {
  redirectUrl: "https://app.blocks.com/callback?code=abc123&state=oidc-state-abc",
};

export const mockOidcFlowAccountRecoverPayload = {
  email: "test@blocks.com",
  projectKey: TEST_PROJECT_KEY,
};

export const mockOidcFlowAccountRecoverResponse = {
  isSuccess: true,
};

export const mockRefreshTokenStorage = JSON.stringify({
  access_token: "old-access-token",
  refresh_token: "old-refresh-token",
  token_type: "Bearer",
  expires_in: 3600,
});

export const mockRefreshedTokenResponse = {
  access_token: "new-access-token",
  refresh_token: "new-refresh-token",
  token_type: "Bearer",
  expires_in: 3600,
};
