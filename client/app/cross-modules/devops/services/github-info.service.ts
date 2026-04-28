import { http } from "@/lib/http-client";
import { CLOUD_BUILD_ENDPOINTS } from "../constants/endpoint.constant";
import { IBuildApiResponse } from "../models/deployed-logs";
import {
  IRepository,
  IBranch,
  ICloneRepo,
  IRepositoryUser,
  IBranchMatchResponse,
} from "../models/github-info";
import {
  CardRepoAndBranchesResponse,
  IChangeRepoSpecs,
  IChangeSettings,
  IManualDeploymentPayload,
} from "../models/utils";

export class GithubInfoService {
  async verifyAuthorization(code: string, projectKey: string): Promise<string> {
    const url = `${CLOUD_BUILD_ENDPOINTS.ACCESS_TOKEN}?code=${encodeURIComponent(code)}&ProjectKey=${encodeURIComponent(projectKey)}`;
    return http.get(url);
  }

  async checkAlreadyAuthorization(): Promise<{
    isSuccess: boolean;
  }> {
    const url = CLOUD_BUILD_ENDPOINTS.IS_AUTHORIZED;
    return http.get(url);
  }

  async revokeAccess(): Promise<{
    isSuccess: boolean;
  }> {
    const url = CLOUD_BUILD_ENDPOINTS.REMOVE_AUTHORIZATION;
    return http.post(url, {});
  }

  async removeAuthorization(): Promise<{
    isSuccess: boolean;
  }> {
    const url = CLOUD_BUILD_ENDPOINTS.REMOVE_ACCESS_TOKEN;
    return http.post(url, {});
  }

  async getGithubRepos(
    projectKey: string,
    search?: string,
    pageNumber?: number,
    pageSize?: number,
  ): Promise<{
    data: {
      items: IRepository[];
      total_count: number;
    };
    message: string | null;
    statusCode: number;
    errors: unknown;
    isSuccess: boolean;
  }> {
    const url = `${CLOUD_BUILD_ENDPOINTS.GITHUB_REPOS}?ProjectKey=${encodeURIComponent(projectKey)}${
      search ? `&search=${encodeURIComponent(search)}` : ""
    }${pageNumber ? `&pageNumber=${pageNumber}` : ""}${pageSize ? `&pageSize=${pageSize}` : ""}`;
    return http.get(url);
  }

  async getRepositoryUser(projectKey: string): Promise<IRepositoryUser> {
    const url = `${CLOUD_BUILD_ENDPOINTS.GITHUB_USER}?ProjectKey=${encodeURIComponent(projectKey)}`;
    return http.get(url);
  }

  async getGithubBranches(repo: string, projectKey: string): Promise<IBranch[]> {
    const url = `${CLOUD_BUILD_ENDPOINTS.GITHUB_BRANCHES}?repo=${encodeURIComponent(repo)}&ProjectKey=${encodeURIComponent(projectKey)}`;
    return http.get(url);
  }

  async getRepoAndGitBranchMatch(
    repoId: string,
    projectKey: string,
  ): Promise<IBranchMatchResponse> {
    const url = `${CLOUD_BUILD_ENDPOINTS.GITHUB_BRANCH_EXISTS}?repoId=${encodeURIComponent(repoId)}&ProjectKey=${encodeURIComponent(projectKey)}`;
    return http.get(url);
  }

  async cloneGithubRepo(payload: ICloneRepo) {
    const url = CLOUD_BUILD_ENDPOINTS.BUILD_BUILD;
    return http.post<any>(url, payload);
  }

  async repoInitialDeploy(payload: any) {
    const url = CLOUD_BUILD_ENDPOINTS.RUN_BUILD;
    return http.post<any>(url, payload);
  }

  async manualDeploy(payload: IManualDeploymentPayload) {
    const url = CLOUD_BUILD_ENDPOINTS.MANUAL;
    return http.post<any>(url, payload);
  }

  async getSpecs() {
    const url = CLOUD_BUILD_ENDPOINTS.SETTINGS;
    return http.get(url);
  }

  async getAllRepos(projectKey: string): Promise<CardRepoAndBranchesResponse[]> {
    const url = `${CLOUD_BUILD_ENDPOINTS.REPOS}?ProjectKey=${encodeURIComponent(projectKey)}`;
    return http.get(url);
  }

  async getAllRepoBuilds(projectKey: string): Promise<any> {
    const url = `${CLOUD_BUILD_ENDPOINTS.REPOS}?ProjectKey=${encodeURIComponent(projectKey)}`;
    return http.get(url);
  }

  async getAllProjects(projectKey: string): Promise<any> {
    const url = `${CLOUD_BUILD_ENDPOINTS.REPOS_LIST}?ProjectKey=${encodeURIComponent(projectKey)}`;
    return http.get(url);
  }

  async getRepoDetails(projectKey: string, repoId: string): Promise<any> {
    const url = `${CLOUD_BUILD_ENDPOINTS.REPO_DETAILS}?ProjectKey=${encodeURIComponent(projectKey)}&RepoId=${encodeURIComponent(repoId)}`;
    return http.get(url);
  }

  async getCardRepoAndBranches(buildId: string, projectKey: string): Promise<IBuildApiResponse> {
    const url = `${CLOUD_BUILD_ENDPOINTS.BUILD}?buildId=${encodeURIComponent(buildId)}&ProjectKey=${encodeURIComponent(projectKey)}`;
    return http.get(url);
  }

  async changeBuildSpecs(payload: IChangeSettings) {
    const url = CLOUD_BUILD_ENDPOINTS.BUILD;
    return http.put(url, payload);
  }

  async changeRepoSpecs(payload: IChangeRepoSpecs) {
    const url = CLOUD_BUILD_ENDPOINTS.SETTINGS;
    return http.post(url, payload);
  }

  async changeRepoSettings(payload: IChangeSettings) {
    const url = CLOUD_BUILD_ENDPOINTS.SETTINGS;
    return http.put(url, payload);
  }

  async getBuildLogs(repoId: string, projectKey: string): Promise<IBuildApiResponse> {
    const url = `${CLOUD_BUILD_ENDPOINTS.RUN_BUILD}?repoId=${repoId}&ProjectKey=${encodeURIComponent(projectKey)}`;
    return http.get(url);
  }

  async getRepoCardsAndBranches(projectKey: string): Promise<CardRepoAndBranchesResponse> {
    const url = `${CLOUD_BUILD_ENDPOINTS.GITHUB_REPOS}?ProjectKey=${encodeURIComponent(projectKey)}`;
    return http.get(url);
  }
}

export const githubInfoService = new GithubInfoService();
