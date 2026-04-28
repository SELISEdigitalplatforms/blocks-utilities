/**
 * Shared mock factory functions for IDP tests.
 *
 * IMPORTANT: vi.mock() is hoisted by Vitest and MUST be called directly in each
 * test file — it cannot be imported from a shared file. These factories provide
 * the mock return values to reduce duplication across test files.
 *
 * Factory names use the `mock` prefix so Vitest's hoisting allows referencing them.
 *
 * Usage in test files (vi.mock calls must be at the top level of the test file):
 *
 *   vi.mock("@/store/useProjectStore", () => mockProjectStoreFactory());
 *   vi.mock("@/hooks/use-toast", () => mockToastFactory());
 *   vi.mock("@/lib/http-client", () => mockHttpClientFactory());
 *   vi.mock("@blocks-idp/authentication/services/auth.service", () => mockAuthServiceFactory());
 *   vi.mock("@blocks-idp/iam/services/user.service", () => mockUserServiceFactory());
 */
import { vi } from "vitest";

// ─── Authentication Service Factories ────────────────────────────────────────

export const mockAuthServiceFactory = () => ({
  authService: {
    signinByEmail: vi.fn(),
    verifyMfa: vi.fn(),
    signupByEmail: vi.fn(),
    logout: vi.fn(),
  },
});

export const mockAuthClientsServiceFactory = () => ({
  authClientService: {
    clients: {
      getClientCredentials: vi.fn(),
      saveClientCredential: vi.fn(),
      deleteClientCredential: vi.fn(),
    },
  },
});

export const mockAuthOidcServiceFactory = () => ({
  authOidc: {
    clients: {
      getOidcCredentials: vi.fn(),
      getOidcCredential: vi.fn(),
      saveOidcCredential: vi.fn(),
      deleteOidcCredential: vi.fn(),
    },
  },
});

export const mockAuthConfigServiceFactory = () => ({
  AuthConfiguration: vi.fn().mockImplementation(() => ({
    getConfig: vi.fn(),
    saveAuthConfig: vi.fn(),
  })),
});

export const mockAuthenticationServiceFactory = () => ({
  authenticationService: {
    configuration: {
      getConfig: vi.fn(),
      saveAuthConfig: vi.fn(),
    },
  },
});

export const mockJwtClaimServiceFactory = () => ({
  jwtClaimServices: {
    addJwtClaim: vi.fn(),
  },
});

export const mockOAuthServiceFactory = () => ({
  oauthService: {
    getSocialLoginEndpoint: vi.fn(),
    signinBySSO: vi.fn(),
  },
});

export const mockSsoServiceFactory = () => ({
  ssoService: {
    getSsoCredentials: vi.fn(),
    getSsoCredentialId: vi.fn(),
    saveSsoCredential: vi.fn(),
    deleteSsoCredential: vi.fn(),
    updateSsoCredentialStatus: vi.fn(),
    saveBlocksSsoCredential: vi.fn(),
    getBlocksSsoCredential: vi.fn(),
  },
});

// ─── IAM Service Factories ──────────────────────────────────────────────────

export const mockUserServiceFactory = () => ({
  userService: {
    getUsers: vi.fn(),
    getUser: vi.fn(),
    getUserById: vi.fn(),
    addUser: vi.fn(),
    updateUser: vi.fn(),
    getSignUpSetting: vi.fn(),
    saveSignUpSetting: vi.fn(),
    saveRolesAndPermissions: vi.fn(),
    getSessions: vi.fn(),
    getHistories: vi.fn(),
    getPats: vi.fn(),
    generatePats: vi.fn(),
    getUserRoles: vi.fn(),
    getUserPermissions: vi.fn(),
    accountDeactivate: vi.fn(),
    account: {
      accountActivation: vi.fn(),
      accountResendActivation: vi.fn(),
      accountRecover: vi.fn(),
      accountResetPassword: vi.fn(),
      checkActivationCodeExpiration: vi.fn(),
    },
  },
});

export const mockRoleServiceFactory = () => ({
  roleService: {
    getRoles: vi.fn(),
    getRoleById: vi.fn(),
    addRole: vi.fn(),
    updateRole: vi.fn(),
    setRoles: vi.fn(),
  },
});

export const mockPermissionServiceFactory = () => ({
  permissionService: {
    getPermissions: vi.fn(),
    getPermissionById: vi.fn(),
    addPermission: vi.fn(),
    updatePermission: vi.fn(),
    getResourceGroup: vi.fn(),
  },
});

export const mockOrganizationServiceFactory = () => ({
  organizationService: {
    getOrganizations: vi.fn(),
    getOrganizationById: vi.fn(),
    saveOrganization: vi.fn(),
    getOrganizationConfig: vi.fn(),
    saveOrganizationConfig: vi.fn(),
  },
});

export const mockIamConfigurationServiceFactory = () => ({
  configurationService: {
    getIamConfiguration: vi.fn(),
    saveIamConfiguration: vi.fn(),
  },
});

export const mockIamServiceFactory = () => ({
  iamService: {
    organization: {
      getOrganizations: vi.fn(),
      getOrganizationById: vi.fn(),
      saveOrganization: vi.fn(),
      getOrganizationConfig: vi.fn(),
      saveOrganizationConfig: vi.fn(),
    },
    permission: {
      getPermissions: vi.fn(),
      getPermissionById: vi.fn(),
      addPermission: vi.fn(),
      updatePermission: vi.fn(),
      getResourceGroup: vi.fn(),
    },
  },
});

// ─── Captcha Service Factory ────────────────────────────────────────────────

export const mockCaptchaServiceFactory = () => ({
  captchaService: {
    getCaptchaConfigs: vi.fn(),
    saveCaptcha: vi.fn(),
    updateCaptchaConfigStatus: vi.fn(),
  },
});

// ─── MFA Service Factory ────────────────────────────────────────────────────

export const mockMfaServiceFactory = () => ({
  mfaService: {
    getConfigurations: vi.fn(),
    saveMFAConfiguration: vi.fn(),
    generateUserMfaOTP: vi.fn(),
    configureUserMFA: vi.fn(),
    setupUserTotp: vi.fn(),
    verifyOtp: vi.fn(),
    resendOtp: vi.fn(),
    disableMFA: vi.fn(),
  },
});
