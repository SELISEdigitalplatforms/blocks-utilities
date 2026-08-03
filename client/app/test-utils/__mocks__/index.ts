import { vi } from "vitest";

export * from "./data.mock";

/**
 * Factory for mocking `@/lib/http-client`.
 *
 * Returns a single shared `http` mock that is reused for every entry in
 * `serviceInstances`, so a call made through `serviceInstances.idpService.post`
 * is recorded on the same spy the tests assert against. The shared object is
 * also exposed on `globalThis.http`, because the service tests reference a bare
 * `http` identifier (resolved against the global scope) without importing it.
 */
export const mockHttpClientFactory = () => {
  const http = {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
    stream: vi.fn(),
  };

  (globalThis as Record<string, unknown>).http = http;

  return {
    http,
    serviceInstances: {
      idpService: http,
      logicService: http,
    },
    HttpClient: class MockHttpClient {},
    HttpError: class MockHttpError extends Error {
      status: number;
      errors: Record<string, string | string[]>;
      constructor(
        status: number,
        error: { errors: Record<string, string | string[]> },
      ) {
        super(String(error));
        this.status = status;
        this.errors = error.errors;
      }
    },
  };
};

/**
 * Default selected-project shape returned by the mocked `useProjectStore`.
 * `tenantId` matches `TEST_TENANT_ID` so hooks that derive a project key from
 * the store see the expected value.
 */
export const mockSelectedProject = {
  itemId: "test-project-id",
  tenantId: "test-tenant-id-123",
  tenantGroupId: "test-tenant-group-id",
  name: "Test Project",
  applicationDomain: "https://test.seliseblocks.com",
  customDomain: "",
  isProduction: false,
  isCookieEnable: false,
  isDomainVerified: false,
  cookieDomain: "",
  isDisabled: false,
  environment: "dev",
  tenantSlug: "test-project",
  createdDate: "2025-01-01T00:00:00Z",
  lastUpdatedDate: "2025-01-01T00:00:00Z",
  createdBy: "user-id",
  lastUpdatedBy: "user-id",
  organizationIds: [],
  tags: [],
};

export const mockProjectStoreFactory = () => ({
  useProjectStore: vi.fn(() => ({
    selectedProject: mockSelectedProject,
    selectedTenantGroup: "test-tenant-group-id",
    projects: [],
    setSelectedProject: vi.fn(),
    resetSelectedProject: vi.fn(),
    setProjects: vi.fn(),
    resetProject: vi.fn(),
    reset: vi.fn(),
    setTennantGroup: vi.fn(),
    resetTennantGroup: vi.fn(),
  })),
});

export const mockToastFactory = () => ({
  useToast: vi.fn(() => ({
    toast: vi.fn(),
    dismiss: vi.fn(),
    toasts: [],
  })),
  toast: vi.fn(),
  showSuccessToast: vi.fn(),
  showInfoToast: vi.fn(),
  showErrorToast: vi.fn(),
});
