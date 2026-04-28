import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { githubInfoService } from "../services/github-info.service";
import { IBuildApiResponse } from "../models/deployed-logs";
import {
  IChangeRepoSpecs,
  IChangeSettings,
  IManualDeploymentPayload,
} from "../models/utils";
import { useProjectStore } from "@/store/useProjectStore";

export const useGithubVerification = (code: string) => {
  const projectKey = useProjectStore().selectedProject?.tenantId || "";

  return useQuery({
    queryKey: ["github-verification", code],
    queryFn: () => githubInfoService.verifyAuthorization(code, projectKey),
    enabled: !!code && !!projectKey,
  });
};

export const useValidateAuthorization = () => {
  return useQuery({
    queryKey: ["verify-auth"],
    queryFn: () => githubInfoService.checkAlreadyAuthorization(),
    retry: false,
  });
};

export const useRevokeAccess = () => {
  return useQuery({
    queryKey: ["revoke-access"],
    queryFn: () => githubInfoService.revokeAccess(),
    retry: false,
    enabled: false, // Only run when explicitly called
  });
};

export const useGetGithubRepos = (
  isVerificationSuccessful: boolean,
  search?: string,
  page?: number,
  perPage?: number,
) => {
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  return useQuery({
    queryKey: ["github-repos", isVerificationSuccessful, search, page, perPage],
    queryFn: () => githubInfoService.getGithubRepos(projectKey, search, page, perPage),
    enabled: isVerificationSuccessful && !!projectKey,
    retry: false,
    staleTime: 0, // Always fetch fresh data
    refetchOnMount: true,
    refetchOnWindowFocus: false,
  });
};

export const useGetRepositoryUser = (isVerificationSuccessful: boolean) => {
  const projectKey = useProjectStore().selectedProject?.tenantId || "";

  return useQuery({
    queryKey: ["repository-user", isVerificationSuccessful],
    queryFn: () => githubInfoService.getRepositoryUser(projectKey),
    enabled: isVerificationSuccessful && !!projectKey,
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
};

export const useRemoveAuthorization = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationKey: ["remove-authorization"],
    mutationFn: githubInfoService.removeAuthorization,
    onSuccess: () => {
      console.log("Authorization removed successfully");
      queryClient.setQueryData(["verify-auth"], () => undefined);
      queryClient.setQueryData(["github-repos"], () => []);
      queryClient.setQueryData(["repository-user"], () => undefined);
    },
  });
};

export const useGithubBranches = (repo: string) => {
  const projectKey = useProjectStore().selectedProject?.tenantId || "";

  return useQuery({
    queryKey: ["github-branches", repo],
    queryFn: () => githubInfoService.getGithubBranches(repo, projectKey),
    enabled: !!repo && !!projectKey,
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
};

export const useRepoAndGitBranchMatch = (repoId: string, enabled: boolean = true) => {
  const projectKey = useProjectStore().selectedProject?.tenantId || "";

  return useQuery({
    queryKey: ["git-branch-match", repoId],
    queryFn: () => githubInfoService.getRepoAndGitBranchMatch(repoId, projectKey),
    enabled: !!repoId && enabled && !!projectKey,
    retry: false,
    refetchOnMount: true,
  });
};

export const useGetAllProjects = (
  projectId: string,
  options?: {
    refetchOnMount: boolean;
    refetchOnWindowFocus: boolean;
    forceRefresh?: boolean;
  },
) => {
  return useQuery({
    queryKey: ["projects", projectId],
    queryFn: () => {
      if (!projectId) {
        throw new Error("Project ID is required");
      }
      return githubInfoService.getAllProjects(projectId);
    },
    enabled: !!projectId,
    retry: false,
    staleTime: options?.forceRefresh ? 0 : 5 * 60 * 1000,
    refetchOnMount: options?.refetchOnMount ? "always" : true,
    refetchOnWindowFocus: options?.refetchOnWindowFocus ?? false,
  });
};

export const useGetAllRepoBuilds = (
  projectId: string,
  options?: {
    refetchOnMount: boolean;
    refetchOnWindowFocus: boolean;
    forceRefresh?: boolean;
  },
) => {
  return useQuery({
    queryKey: ["repo-builds", projectId],
    queryFn: () => {
      if (!projectId) {
        throw new Error("Project ID is required");
      }
      return githubInfoService.getAllRepoBuilds(projectId);
    },
    enabled: !!projectId,
    retry: false,
    staleTime: options?.forceRefresh ? 0 : 5 * 60 * 1000,
    refetchOnMount: options?.refetchOnMount ? "always" : true,
    refetchOnWindowFocus: options?.refetchOnWindowFocus ?? false,
  });
};

export const useGetRepoDetails = (
  projectKey: string,
  repoId: string,
  options?: {
    refetchOnMount: boolean;
    refetchOnWindowFocus: boolean;
    forceRefresh?: boolean;
  },
) => {
  return useQuery({
    queryKey: ["repo-details", projectKey, repoId],
    queryFn: () => {
      if (!projectKey || !repoId) {
        throw new Error("Project Key and Repo ID are required");
      }
      return githubInfoService.getRepoDetails(projectKey, repoId);
    },
    enabled: !!projectKey && !!repoId,
    staleTime: options?.forceRefresh ? 0 : 5 * 60 * 1000,
    refetchOnMount: options?.refetchOnMount ? "always" : true,
    refetchOnWindowFocus: options?.refetchOnWindowFocus ?? false,
    retry: false,
  });
};

export const useInitialRepoDeployment = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: IChangeRepoSpecs) => githubInfoService.repoInitialDeploy(payload),
    onSuccess: (data) => {
      queryClient.setQueryData(["repo-id"], data);
      queryClient.invalidateQueries({ queryKey: ["repo-details"] });
    },
    onError: (error) => {
      console.error("Repo initial deployment failed:", error);
    },
  });
};

export const useManualDeployment = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (options: IManualDeploymentPayload) => githubInfoService.manualDeploy(options),
    onSuccess: (data) => {
      queryClient.setQueryData(["repo-id"], data);
      queryClient.invalidateQueries({ queryKey: ["github-repos"] });
    },
    onError: (error) => {
      console.error("Manual Clone failed:", error);
    },
  });
};

export const useGetSpecs = () => {
  return useQuery({
    queryKey: ["specs"],
    queryFn: () => githubInfoService.getSpecs(),
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
};

export const useGetCardProjectAndBranch = (buildId: string) => {
  const projectKey = useProjectStore().selectedProject?.tenantId || "";

  return useQuery<IBuildApiResponse>({
    queryKey: ["project-repo", buildId],
    queryFn: () => {
      if (!buildId) {
        throw new Error("Project ID is required");
      }
      return githubInfoService.getCardRepoAndBranches(buildId, projectKey);
    },
    enabled: !!buildId && !!projectKey,
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
};

export const useChangeBuildSpecs = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: IChangeSettings) => githubInfoService.changeBuildSpecs(payload),
    onSuccess: (data) => {
      queryClient.setQueryData(["build-specs"], data);
      queryClient.invalidateQueries({ queryKey: ["repo-specs"] });
      queryClient.invalidateQueries({ queryKey: ["repo-builds"] });
    },
    onError: (error) => {
      console.error("Build specs change failed:", error);
    },
  });
};

export const useChangeRepoSpecs = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: IChangeRepoSpecs) => githubInfoService.changeRepoSpecs(payload),
    onSuccess: (data) => {
      queryClient.setQueryData(["repo-specs"], data);
      queryClient.invalidateQueries({ queryKey: ["repo-builds"] });
    },
    onError: (error) => {
      console.error("Build specs change failed:", error);
    },
  });
};
