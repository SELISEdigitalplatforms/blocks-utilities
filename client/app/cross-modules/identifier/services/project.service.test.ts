import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import {
  mockProjectGroup,
  mockGetAssetsResponse,
  mockGetEnvRepositoriesResponse,
  mockGetProjectResponse,
  mockCreateProjectResponse,
  mockUpdateProjectResponse,
  mockDisableProjectResponse,
  mockSuccessResponse,
  mockLoginOptionsResponse,
  mockMigrationInitiateResponse,
  mockMigrationVerifyResponse,
  mockMigrationStatusResponse,
  mockPublicCertificateResponse,
  mockValidateCNameResponse,
  mockGetSubscriptionUsageResponse,
  mockResource,
} from "../test-utils/__mocks__";
import { http } from "@/lib/http-client";
import {
  PROJECT_ENDPOINTS,
  DOMAIN_ENDPOINTS,
  MIGRATION_ENDPOINTS,
  SUBSCRIPTION_ENDPOINTS,
  CLOUD_BUILD_ENDPOINTS,
} from "@blocks-identifier/constants/endpoint.constant";
import { ProjectService } from "./project.service";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("ProjectService", () => {
  let service: ProjectService;

  beforeEach(() => {
    service = new ProjectService();
  });

  // ─── getProjects ────────────────────────────────────────────────────────────

  describe("getProjects", () => {
    it("should call correct endpoint with query params", async () => {
      vi.mocked(http.get).mockResolvedValue([mockProjectGroup]);

      const result = await service.getProjects(1, 10, "tenant-group-1");

      expect(http.get).toHaveBeenCalledWith(
        `${PROJECT_ENDPOINTS.GETS}?page=1&pageSize=10&tenantGroupId=tenant-group-1`,
      );
      expect(result).toEqual([mockProjectGroup]);
    });

    it("should handle different pagination params", async () => {
      vi.mocked(http.get).mockResolvedValue([]);

      await service.getProjects(3, 25, "group-2");

      expect(http.get).toHaveBeenCalledWith(
        `${PROJECT_ENDPOINTS.GETS}?page=3&pageSize=25&tenantGroupId=group-2`,
      );
    });

    it("should handle API errors", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Failed to fetch projects"));

      await expect(service.getProjects(1, 10, "group")).rejects.toThrow("Failed to fetch projects");
    });
  });

  // ─── getAssets ──────────────────────────────────────────────────────────────

  describe("getAssets", () => {
    it("should call correct endpoint with tenantGroupId", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetAssetsResponse);

      const result = await service.getAssets("tenant-group-1");

      expect(http.get).toHaveBeenCalledWith(
        `${PROJECT_ENDPOINTS.GET_ASSET}?TenantGroupId=tenant-group-1`,
      );
      expect(result).toEqual(mockGetAssetsResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Failed to fetch assets"));

      await expect(service.getAssets("group")).rejects.toThrow("Failed to fetch assets");
    });
  });

  // ─── addAssets ──────────────────────────────────────────────────────────────

  describe("addAssets", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = { tenantGroupId: "group-1", resource: mockResource };
      const result = await service.addAssets(payload);

      expect(http.post).toHaveBeenCalledWith(PROJECT_ENDPOINTS.ADD_ASSET, payload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Failed to add asset"));

      await expect(
        service.addAssets({ tenantGroupId: "group-1", resource: mockResource }),
      ).rejects.toThrow("Failed to add asset");
    });
  });

  // ─── getEnvRepositories ─────────────────────────────────────────────────────

  describe("getEnvRepositories", () => {
    it("should call correct endpoint with projectkey", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetEnvRepositoriesResponse);

      const result = await service.getEnvRepositories("proj-key-1");

      expect(http.get).toHaveBeenCalledWith(
        `${CLOUD_BUILD_ENDPOINTS.REPOS_LIST}?projectkey=proj-key-1`,
      );
      expect(result).toEqual(mockGetEnvRepositoriesResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Failed to fetch repos"));

      await expect(service.getEnvRepositories("proj-key")).rejects.toThrow("Failed to fetch repos");
    });
  });

  // ─── repoUpdate ─────────────────────────────────────────────────────────────

  describe("repoUpdate", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = {
        projectKey: "proj-key",
        projectEnv: "dev",
        repoWithDomains: [
          { repoId: "repo-1", repoUrl: "https://github.com/repo", customDeploymentDomain: "" },
        ],
      };
      const result = await service.repoUpdate(payload);

      expect(http.post).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.REPO_UPDATE, payload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Repo update failed"));

      await expect(
        service.repoUpdate({
          projectKey: "key",
          projectEnv: "dev",
          repoWithDomains: [],
        }),
      ).rejects.toThrow("Repo update failed");
    });
  });

  // ─── getProject ─────────────────────────────────────────────────────────────

  describe("getProject", () => {
    it("should call correct endpoint with projectId", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetProjectResponse);

      const result = await service.getProject({ projectId: "proj-123" });

      expect(http.get).toHaveBeenCalledWith(`${PROJECT_ENDPOINTS.GET}?projectId=proj-123`);
      expect(result).toEqual(mockGetProjectResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Project not found"));

      await expect(service.getProject({ projectId: "bad" })).rejects.toThrow("Project not found");
    });
  });

  // ─── createProject ─────────────────────────────────────────────────────────

  describe("createProject", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockCreateProjectResponse);

      const payload = {
        name: "New Project",
        isAcceptBlocksTerms: true,
        isUseBlocksExclusively: true,
        isProduction: false,
        resources: [],
        applicationContexts: [],
      };
      const result = await service.createProject(payload);

      expect(http.post).toHaveBeenCalledWith(PROJECT_ENDPOINTS.CREATE, payload);
      expect(result).toEqual(mockCreateProjectResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Create failed"));

      await expect(
        service.createProject({
          name: "Test",
          isAcceptBlocksTerms: true,
          isUseBlocksExclusively: false,
          isProduction: false,
          resources: [],
          applicationContexts: [],
        }),
      ).rejects.toThrow("Create failed");
    });
  });

  // ─── validateCNameProject ───────────────────────────────────────────────────

  describe("validateCNameProject", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockValidateCNameResponse);

      const payload = { projectKey: "proj-key", cookieDomain: "test.com" };
      const result = await service.validateCNameProject(payload);

      expect(http.post).toHaveBeenCalledWith(DOMAIN_ENDPOINTS.CONFIGURE, payload);
      expect(result).toEqual(mockValidateCNameResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("CNAME validation failed"));

      await expect(
        service.validateCNameProject({ projectKey: "key", cookieDomain: "bad.com" }),
      ).rejects.toThrow("CNAME validation failed");
    });
  });

  // ─── updateProject ─────────────────────────────────────────────────────────

  describe("updateProject", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockUpdateProjectResponse);

      const payload = {
        projectKey: "key",
        name: "Updated Project",
        applicationDomain: "example.com",
      };
      const result = await service.updateProject(payload);

      expect(http.post).toHaveBeenCalledWith(PROJECT_ENDPOINTS.UPDATE, payload);
      expect(result).toEqual(mockUpdateProjectResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Update failed"));

      await expect(
        service.updateProject({ projectKey: "key", name: "n", applicationDomain: "d" }),
      ).rejects.toThrow("Update failed");
    });
  });

  // ─── disableProject ────────────────────────────────────────────────────────

  describe("disableProject", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockDisableProjectResponse);

      const payload = { projectKey: "proj-key" };
      const result = await service.disableProject(payload);

      expect(http.post).toHaveBeenCalledWith(PROJECT_ENDPOINTS.DISABLE, payload);
      expect(result).toEqual(mockDisableProjectResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Disable failed"));

      await expect(service.disableProject({ projectKey: "key" })).rejects.toThrow("Disable failed");
    });
  });

  // ─── getProjectLoginOption ──────────────────────────────────────────────────

  describe("getProjectLoginOption", () => {
    it("should call correct endpoint", async () => {
      vi.mocked(http.get).mockResolvedValue(mockLoginOptionsResponse);

      const result = await service.getProjectLoginOption();

      expect(http.get).toHaveBeenCalledWith(PROJECT_ENDPOINTS.GET_LOGIN_OPTIONS);
      expect(result).toEqual(mockLoginOptionsResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Login options failed"));

      await expect(service.getProjectLoginOption()).rejects.toThrow("Login options failed");
    });
  });

  // ─── initiateMigration ─────────────────────────────────────────────────────

  describe("initiateMigration", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockMigrationInitiateResponse);

      const payload = {
        projectKey: "proj-key",
        targetedProjectKey: "target-key",
        tenantGroupId: "group-1",
        services: [{ shouldOverWriteExistingData: true, serviceName: 1 }],
      };
      const result = await service.initiateMigration(payload);

      expect(http.post).toHaveBeenCalledWith(MIGRATION_ENDPOINTS.MIGRATE, payload);
      expect(result).toEqual(mockMigrationInitiateResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Migration failed"));

      await expect(
        service.initiateMigration({
          projectKey: "key",
          targetedProjectKey: "target",
          tenantGroupId: "group",
          services: [],
        }),
      ).rejects.toThrow("Migration failed");
    });
  });

  // ─── verifyMigration ───────────────────────────────────────────────────────

  describe("verifyMigration", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockMigrationVerifyResponse);

      const payload = { verificationId: "verify-1", verificationCode: "123456" };
      const result = await service.verifyMigration(payload);

      expect(http.post).toHaveBeenCalledWith(MIGRATION_ENDPOINTS.VERIFY, payload);
      expect(result).toEqual(mockMigrationVerifyResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Verification failed"));

      await expect(
        service.verifyMigration({ verificationId: "v", verificationCode: "bad" }),
      ).rejects.toThrow("Verification failed");
    });
  });

  // ─── getMigrationStatus ─────────────────────────────────────────────────────

  describe("getMigrationStatus", () => {
    it("should call correct endpoint with tenantGroupId", async () => {
      vi.mocked(http.get).mockResolvedValue(mockMigrationStatusResponse);

      const result = await service.getMigrationStatus("group-1");

      expect(http.get).toHaveBeenCalledWith(
        `${MIGRATION_ENDPOINTS.GET_STATUS}?tenantGroupId=group-1`,
      );
      expect(result).toEqual(mockMigrationStatusResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Migration status failed"));

      await expect(service.getMigrationStatus("group")).rejects.toThrow("Migration status failed");
    });
  });

  // ─── savePublicCertificate ──────────────────────────────────────────────────

  describe("savePublicCertificate", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockUpdateProjectResponse);

      const payload = {
        projectKey: "proj-key",
        publicCertificatePassword: "pass",
        issuer: "https://auth.example.com",
        audiences: ["https://api.example.com"],
        publicCertificatePath: "/certs/cert.pem",
        jwksUrl: "https://auth.example.com/.well-known/jwks.json",
        providerName: "Auth0",
      };
      const result = await service.savePublicCertificate(payload);

      expect(http.post).toHaveBeenCalledWith(PROJECT_ENDPOINTS.UPDATE_TOKEN_VALIDATION, payload);
      expect(result).toEqual(mockUpdateProjectResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Save certificate failed"));

      await expect(
        service.savePublicCertificate({
          projectKey: "key",
          publicCertificatePassword: "",
          issuer: "",
          audiences: [],
          publicCertificatePath: "",
          jwksUrl: "",
          providerName: "",
        }),
      ).rejects.toThrow("Save certificate failed");
    });
  });

  // ─── getPublicCertificateInformation ────────────────────────────────────────

  describe("getPublicCertificateInformation", () => {
    it("should call correct endpoint with projectKey", async () => {
      vi.mocked(http.get).mockResolvedValue(mockPublicCertificateResponse);

      const result = await service.getPublicCertificateInformation("proj-key-1");

      expect(http.get).toHaveBeenCalledWith(
        `${PROJECT_ENDPOINTS.GET_TOKEN_VALIDATION}?ProjectKey=proj-key-1`,
      );
      expect(result).toEqual(mockPublicCertificateResponse);
    });

    it("should return null when no certificate is configured", async () => {
      vi.mocked(http.get).mockResolvedValue(null);

      const result = await service.getPublicCertificateInformation("proj-key");

      expect(result).toBeNull();
    });

    it("should handle API errors", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Certificate fetch failed"));

      await expect(service.getPublicCertificateInformation("key")).rejects.toThrow(
        "Certificate fetch failed",
      );
    });
  });

  // ─── validateJwksUrl ────────────────────────────────────────────────────────

  describe("validateJwksUrl", () => {
    const originalFetch = global.fetch;

    beforeEach(() => {
      global.fetch = vi.fn();
    });

    afterEach(() => {
      global.fetch = originalFetch;
    });

    it("should return isValid true for a valid JWKS URL", async () => {
      const mockJwks = { keys: [{ kty: "RSA", kid: "key-1" }] };
      vi.mocked(global.fetch).mockResolvedValue({
        ok: true,
        headers: new Headers({ "content-type": "application/json" }),
        json: () => Promise.resolve(mockJwks),
      } as Response);

      const result = await service.validateJwksUrl(
        "https://auth.example.com/.well-known/jwks.json",
      );

      expect(result.isValid).toBe(true);
      expect(result.data).toEqual(mockJwks);
    });

    it("should return isValid false for HTTP error response", async () => {
      vi.mocked(global.fetch).mockResolvedValue({
        ok: false,
        status: 404,
        headers: new Headers(),
        json: () => Promise.resolve({}),
      } as Response);

      const result = await service.validateJwksUrl("https://bad-url.com/jwks");

      expect(result.isValid).toBe(false);
      expect(result.error).toContain("Invalid");
    });

    it("should return isValid false for non-JSON response", async () => {
      vi.mocked(global.fetch).mockResolvedValue({
        ok: true,
        headers: new Headers({ "content-type": "text/html" }),
        json: () => Promise.resolve({}),
      } as Response);

      const result = await service.validateJwksUrl("https://example.com/not-json");

      expect(result.isValid).toBe(false);
      expect(result.error).toContain("Invalid");
    });

    it("should return isValid false when keys array is missing", async () => {
      vi.mocked(global.fetch).mockResolvedValue({
        ok: true,
        headers: new Headers({ "content-type": "application/json" }),
        json: () => Promise.resolve({ notKeys: [] }),
      } as Response);

      const result = await service.validateJwksUrl("https://example.com/bad-jwks");

      expect(result.isValid).toBe(false);
      expect(result.error).toContain("Invalid");
    });

    it("should return isValid false when keys array is empty", async () => {
      vi.mocked(global.fetch).mockResolvedValue({
        ok: true,
        headers: new Headers({ "content-type": "application/json" }),
        json: () => Promise.resolve({ keys: [] }),
      } as Response);

      const result = await service.validateJwksUrl("https://example.com/empty-keys");

      expect(result.isValid).toBe(false);
      expect(result.error).toContain("Invalid");
    });

    it("should return isValid false on network error", async () => {
      vi.mocked(global.fetch).mockRejectedValue(new Error("Network error"));

      const result = await service.validateJwksUrl("https://unreachable.com/jwks");

      expect(result.isValid).toBe(false);
      expect(result.error).toContain("Invalid");
    });
  });

  // ─── getJwtClaim ────────────────────────────────────────────────────────────

  describe("getJwtClaim", () => {
    it("should call correct endpoint with query params", async () => {
      const mockResponse = { data: [], errors: null };
      vi.mocked(http.get).mockResolvedValue(mockResponse);

      const payload = { projectKey: "proj-key", itemId: "item-1" };
      const result = await service.getJwtClaim(payload);

      expect(http.get).toHaveBeenCalledWith(
        `${PROJECT_ENDPOINTS.GET_JWT_CLAIMS}?ProjectKey=proj-key&ItemId=item-1`,
      );
      expect(result).toEqual(mockResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("JWT claim fetch failed"));

      await expect(service.getJwtClaim({ projectKey: "key", itemId: "item" })).rejects.toThrow(
        "JWT claim fetch failed",
      );
    });
  });

  // ─── addJwtClaim ────────────────────────────────────────────────────────────

  describe("addJwtClaim", () => {
    it("should call correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = {
        projectKey: "proj-key",
        userId: "u1",
        email: "a@b.com",
        name: "Test",
        userName: "test",
        roles: "admin",
      };
      const result = await service.addJwtClaim(payload);

      expect(http.post).toHaveBeenCalledWith(PROJECT_ENDPOINTS.SAVE_JWT_CLAIMS, payload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("JWT claim save failed"));

      await expect(
        service.addJwtClaim({
          projectKey: "key",
          userId: "u",
          email: "e",
          name: "n",
          userName: "u",
          roles: "r",
        }),
      ).rejects.toThrow("JWT claim save failed");
    });
  });

  // ─── getSubscriptionUsage ──────────────────────────────────────────────────

  describe("getSubscriptionUsage", () => {
    it("should call correct endpoint with projectKey", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetSubscriptionUsageResponse);

      const result = await service.getSubscriptionUsage("proj-key-1");

      expect(http.get).toHaveBeenCalledWith(`${SUBSCRIPTION_ENDPOINTS.GETS}?projectKey=proj-key-1`);
      expect(result).toEqual(mockGetSubscriptionUsageResponse);
    });

    it("should handle API errors", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Subscription usage failed"));

      await expect(service.getSubscriptionUsage("key")).rejects.toThrow(
        "Subscription usage failed",
      );
    });
  });
});
