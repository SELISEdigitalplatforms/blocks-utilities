import type {
  IProject,
  IProjectGroup,
  IEnvRepository,
  IGetProjectResponse,
  IGetSubscriptionUsageResponse,
  ISubscription,
  IMigrationStatusResponse,
  IGetPublicCertificateResponse,
  IResource,
} from "../../models/project.model";

// ─── Mock IDs ─────────────────────────────────────────────────────────────────

const MOCK_PROJECT_ID_1 = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
const MOCK_PROJECT_ID_2 = "b2c3d4e5-f6a7-8901-bcde-f12345678901";
export const MOCK_TENANT_GROUP_ID = "c3d4e5f6-a7b8-9012-cdef-123456789012";
const MOCK_USER_ID_1 = "d4e5f6a7-b8c9-0123-defa-234567890123";
const MOCK_REPO_ID_1 = "d0e1f2a3-b4c5-6789-defa-890123456789";
const MOCK_REPO_ID_2 = "e1f2a3b4-c5d6-7890-efab-901234567890";
const MOCK_SUBSCRIPTION_ID_1 = "f2a3b4c5-d6e7-8901-fabc-012345678901";
const MOCK_VERIFICATION_ID = "a3b4c5d6-e7f8-9012-abcd-123456789abc";
export const TEST_TENANT_ID = "test-tenant-id-123";
export const TEST_PROJECT_KEY = "test-project-key";

// ─── Mock Generic Responses ───────────────────────────────────────────────────

export const mockSuccessResponse = {
  errors: null,
  isSuccess: true,
};

export const mockErrorResponse = {
  errors: { message: "An error occurred" },
  isSuccess: false,
};

export const mockDeleteSuccessResponse = {
  errors: null,
  isSuccess: true,
};

// ─── Mock Project ─────────────────────────────────────────────────────────────

export const mockProject: IProject = {
  itemId: MOCK_PROJECT_ID_1,
  createdDate: "2025-01-01T10:00:00Z",
  lastUpdatedDate: "2025-01-15T14:30:00Z",
  createdBy: MOCK_USER_ID_1,
  lastUpdatedBy: MOCK_USER_ID_1,
  organizationIds: [TEST_TENANT_ID],
  tags: [],
  name: "Test Project",
  applicationDomain: "https://test.seliseblocks.com",
  customDomain: "",
  isProduction: true,
  tenantId: TEST_TENANT_ID,
  isCookieEnable: true,
  isDomainVerified: true,
  cookieDomain: "test.seliseblocks.com",
  isDisabled: false,
  environment: "dev",
  tenantGroupId: MOCK_TENANT_GROUP_ID,
  tenantSlug: "test-project",
};

export const mockProject2: IProject = {
  ...mockProject,
  itemId: MOCK_PROJECT_ID_2,
  name: "Test Project Staging",
  environment: "stg",
};

export const mockProjectGroup: IProjectGroup = {
  tenantGroupId: MOCK_TENANT_GROUP_ID,
  projects: [mockProject, mockProject2],
};

export const mockGetProjectResponse: IGetProjectResponse = {
  data: mockProject,
  errors: null,
};

export const mockCreateProjectResponse = {
  isSuccess: true,
  errors: {},
  tenantGroupId: MOCK_TENANT_GROUP_ID,
};

export const mockUpdateProjectResponse = {
  errors: null,
  isSuccess: true,
};

export const mockDisableProjectResponse = {
  errors: null,
  isSuccess: true,
};

// ─── Mock Resources / Assets ──────────────────────────────────────────────────

export const mockResource: IResource = {
  name: "test-repo",
  link: "https://github.com/test-org/test-repo",
  resourceId: MOCK_REPO_ID_1,
};

export const mockGetAssetsResponse = {
  assets: {
    resources: [mockResource],
    tenantGroupId: MOCK_TENANT_GROUP_ID,
    createdDate: "2025-01-01T10:00:00Z",
    itemId: "mock-item-id",
  },
  totalCount: 1,
  errors: null,
  isSuccess: true,
};

// ─── Mock Repositories ────────────────────────────────────────────────────────

export const mockEnvRepository: IEnvRepository = {
  itemId: MOCK_REPO_ID_1,
  repoName: "test-repo",
  repoUrl: "https://github.com/test-org/test-repo",
  defaultDeploymentUrl: "https://test-repo.seliseblocks.com",
  customDeploymentUrl: "",
  lastDeploymentDate: "2025-01-10T08:00:00Z",
};

export const mockEnvRepository2: IEnvRepository = {
  itemId: MOCK_REPO_ID_2,
  repoName: "test-api",
  repoUrl: "https://github.com/test-org/test-api",
  defaultDeploymentUrl: "https://test-api.seliseblocks.com",
  customDeploymentUrl: "https://api.custom-domain.com",
  lastDeploymentDate: "2025-01-12T09:00:00Z",
};

export const mockGetEnvRepositoriesResponse = {
  data: [mockEnvRepository, mockEnvRepository2],
  errors: null,
  isSuccess: true,
};

// ─── Mock Migration ───────────────────────────────────────────────────────────

export const mockMigrationInitiateResponse = {
  verificationId: MOCK_VERIFICATION_ID,
  isSuccess: true,
};

export const mockMigrationVerifyResponse = {
  isValid: true,
  isSuccess: true,
  errors: null,
};

export const mockMigrationStatusResponse: IMigrationStatusResponse = [
  { targetedProjectKey: TEST_PROJECT_KEY },
];

// ─── Mock Subscription ────────────────────────────────────────────────────────

export const mockSubscription: ISubscription = {
  resource: "People",
  resourceType: null,
  limit: 100,
  usage: 25,
  lifetime: "monthly",
  isActive: true,
  enableAutoRenew: true,
  tenantId: TEST_TENANT_ID,
  type: "standard",
  itemId: MOCK_SUBSCRIPTION_ID_1,
  createdDate: "2025-01-01T10:00:00Z",
  lastUpdatedDate: "2025-01-15T14:30:00Z",
  createdBy: MOCK_USER_ID_1,
  language: "en",
  lastUpdatedBy: MOCK_USER_ID_1,
  organizationIds: [TEST_TENANT_ID],
  tags: [],
};

export const mockGetSubscriptionUsageResponse: IGetSubscriptionUsageResponse = {
  subscriptions: [mockSubscription],
  errors: null,
  isSuccess: true,
};

// ─── Mock Public Certificate ──────────────────────────────────────────────────

export const mockPublicCertificateResponse: IGetPublicCertificateResponse = {
  issuer: "https://auth.example.com",
  audiences: ["https://api.example.com"],
  publicCertificatePath: "/certificates/public.pem",
  jwksUrl: "https://auth.example.com/.well-known/jwks.json",
  cookieKey: null,
  isConfigured: true,
  providerName: "Auth0",
};

// ─── Mock CNAME Validation ────────────────────────────────────────────────────

export const mockValidateCNameResponse = {
  errors: null,
  isSuccess: true,
  isStatusChanged: false,
};

// ─── Mock Login Options ───────────────────────────────────────────────────────

export const mockLoginOptionsResponse = {
  allowedGrantTypes: ["password"],
  ssoInfo: [],
};
