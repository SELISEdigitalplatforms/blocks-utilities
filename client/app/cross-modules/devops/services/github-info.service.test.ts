import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { GithubInfoService } from "./github-info.service";
import { CLOUD_BUILD_ENDPOINTS } from "../constants/endpoint.constant";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("GithubInfoService", () => {
  let service: GithubInfoService;

  beforeEach(() => {
    service = new GithubInfoService();
    vi.clearAllMocks();
  });
  afterEach(() => vi.clearAllMocks());

  it("verifyAuthorization encodes the code and project key", async () => {
    vi.mocked(http.get).mockResolvedValue("token");
    const result = await service.verifyAuthorization("a b", "p&k");
    expect(http.get).toHaveBeenCalledWith(
      `${CLOUD_BUILD_ENDPOINTS.ACCESS_TOKEN}?code=a%20b&ProjectKey=p%26k`,
    );
    expect(result).toBe("token");
  });

  it("checkAlreadyAuthorization gets the is-authorized endpoint", async () => {
    vi.mocked(http.get).mockResolvedValue({ isSuccess: true });
    await service.checkAlreadyAuthorization();
    expect(http.get).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.IS_AUTHORIZED);
  });

  it("revokeAccess posts to the remove-authorization endpoint", async () => {
    vi.mocked(http.post).mockResolvedValue({ isSuccess: true });
    await service.revokeAccess();
    expect(http.post).toHaveBeenCalledWith(
      CLOUD_BUILD_ENDPOINTS.REMOVE_AUTHORIZATION,
      {},
    );
  });

  it("removeAuthorization posts to the remove-access-token endpoint", async () => {
    vi.mocked(http.post).mockResolvedValue({ isSuccess: true });
    await service.removeAuthorization();
    expect(http.post).toHaveBeenCalledWith(
      CLOUD_BUILD_ENDPOINTS.REMOVE_ACCESS_TOKEN,
      {},
    );
  });

  it("getGithubRepos appends optional search and paging params", async () => {
    vi.mocked(http.get).mockResolvedValue({ data: {} });
    await service.getGithubRepos("pk", "term", 2, 20);
    const url = vi.mocked(http.get).mock.calls[0][0] as string;
    expect(url).toContain("ProjectKey=pk");
    expect(url).toContain("search=term");
    expect(url).toContain("pageNumber=2");
    expect(url).toContain("pageSize=20");
  });

  it("getGithubRepos omits optional params when not provided", async () => {
    vi.mocked(http.get).mockResolvedValue({ data: {} });
    await service.getGithubRepos("pk");
    const url = vi.mocked(http.get).mock.calls[0][0] as string;
    expect(url).not.toContain("search=");
    expect(url).not.toContain("pageNumber=");
  });

  it("getRepositoryUser gets the github user endpoint", async () => {
    vi.mocked(http.get).mockResolvedValue({ login: "u" });
    await service.getRepositoryUser("pk");
    expect(http.get).toHaveBeenCalledWith(
      `${CLOUD_BUILD_ENDPOINTS.GITHUB_USER}?ProjectKey=pk`,
    );
  });

  it("getGithubBranches encodes the repo name", async () => {
    vi.mocked(http.get).mockResolvedValue([]);
    await service.getGithubBranches("my repo", "pk");
    expect(http.get).toHaveBeenCalledWith(
      `${CLOUD_BUILD_ENDPOINTS.GITHUB_BRANCHES}?repo=my%20repo&ProjectKey=pk`,
    );
  });

  it("getRepoAndGitBranchMatch builds the branch-exists url", async () => {
    vi.mocked(http.get).mockResolvedValue({ isMatch: true });
    await service.getRepoAndGitBranchMatch("rid", "pk");
    expect(http.get).toHaveBeenCalledWith(
      `${CLOUD_BUILD_ENDPOINTS.GITHUB_BRANCH_EXISTS}?repoId=rid&ProjectKey=pk`,
    );
  });

  it("cloneGithubRepo posts the payload", async () => {
    vi.mocked(http.post).mockResolvedValue({});
    await service.cloneGithubRepo({ repo: "r" } as never);
    expect(http.post).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.BUILD_BUILD, {
      repo: "r",
    });
  });

  it("repoInitialDeploy posts to run-build", async () => {
    vi.mocked(http.post).mockResolvedValue({});
    await service.repoInitialDeploy({ a: 1 });
    expect(http.post).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.RUN_BUILD, {
      a: 1,
    });
  });

  it("manualDeploy posts to manual", async () => {
    vi.mocked(http.post).mockResolvedValue({});
    await service.manualDeploy({ a: 1 } as never);
    expect(http.post).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.MANUAL, {
      a: 1,
    });
  });

  it("getSpecs gets the settings endpoint", async () => {
    vi.mocked(http.get).mockResolvedValue({});
    await service.getSpecs();
    expect(http.get).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.SETTINGS);
  });

  it("getAllRepos, getAllRepoBuilds and getRepoCardsAndBranches hit repo endpoints", async () => {
    vi.mocked(http.get).mockResolvedValue([]);
    await service.getAllRepos("pk");
    await service.getAllRepoBuilds("pk");
    await service.getRepoCardsAndBranches("pk");
    expect(http.get).toHaveBeenCalledTimes(3);
  });

  it("getAllProjects gets the repos-list endpoint", async () => {
    vi.mocked(http.get).mockResolvedValue([]);
    await service.getAllProjects("pk");
    expect(http.get).toHaveBeenCalledWith(
      `${CLOUD_BUILD_ENDPOINTS.REPOS_LIST}?ProjectKey=pk`,
    );
  });

  it("getRepoDetails builds the repo-details url", async () => {
    vi.mocked(http.get).mockResolvedValue({});
    await service.getRepoDetails("pk", "rid");
    expect(http.get).toHaveBeenCalledWith(
      `${CLOUD_BUILD_ENDPOINTS.REPO_DETAILS}?ProjectKey=pk&RepoId=rid`,
    );
  });

  it("getCardRepoAndBranches builds the build url", async () => {
    vi.mocked(http.get).mockResolvedValue({});
    await service.getCardRepoAndBranches("bid", "pk");
    expect(http.get).toHaveBeenCalledWith(
      `${CLOUD_BUILD_ENDPOINTS.BUILD}?buildId=bid&ProjectKey=pk`,
    );
  });

  it("changeBuildSpecs puts to the build endpoint", async () => {
    vi.mocked(http.put).mockResolvedValue({});
    await service.changeBuildSpecs({ a: 1 } as never);
    expect(http.put).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.BUILD, {
      a: 1,
    });
  });

  it("changeRepoSpecs posts to the settings endpoint", async () => {
    vi.mocked(http.post).mockResolvedValue({});
    await service.changeRepoSpecs({ a: 1 } as never);
    expect(http.post).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.SETTINGS, {
      a: 1,
    });
  });

  it("changeRepoSettings puts to the settings endpoint", async () => {
    vi.mocked(http.put).mockResolvedValue({});
    await service.changeRepoSettings({ a: 1 } as never);
    expect(http.put).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.SETTINGS, {
      a: 1,
    });
  });

  it("getBuildLogs builds the run-build url", async () => {
    vi.mocked(http.get).mockResolvedValue({});
    await service.getBuildLogs("rid", "pk");
    expect(http.get).toHaveBeenCalledWith(
      `${CLOUD_BUILD_ENDPOINTS.RUN_BUILD}?repoId=rid&ProjectKey=pk`,
    );
  });
});
