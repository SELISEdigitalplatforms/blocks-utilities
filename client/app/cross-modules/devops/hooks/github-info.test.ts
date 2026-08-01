import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { githubInfoService } from "../services/github-info.service";
import {
  useGithubVerification,
  useValidateAuthorization,
  useRevokeAccess,
  useGetGithubRepos,
  useGetRepositoryUser,
  useRemoveAuthorization,
  useGithubBranches,
  useRepoAndGitBranchMatch,
  useGetAllProjects,
  useGetAllRepoBuilds,
  useGetRepoDetails,
  useInitialRepoDeployment,
  useManualDeployment,
  useGetSpecs,
  useGetCardProjectAndBranch,
  useChangeBuildSpecs,
  useChangeRepoSpecs,
} from "./github-info";

vi.mock("../services/github-info.service", () => ({
  githubInfoService: {
    verifyAuthorization: vi.fn(),
    checkAlreadyAuthorization: vi.fn(),
    revokeAccess: vi.fn(),
    getGithubRepos: vi.fn(),
    getRepositoryUser: vi.fn(),
    removeAuthorization: vi.fn(),
    getGithubBranches: vi.fn(),
    getRepoAndGitBranchMatch: vi.fn(),
    getAllProjects: vi.fn(),
    getAllRepoBuilds: vi.fn(),
    getRepoDetails: vi.fn(),
    repoInitialDeploy: vi.fn(),
    manualDeploy: vi.fn(),
    getSpecs: vi.fn(),
    getCardRepoAndBranches: vi.fn(),
    changeBuildSpecs: vi.fn(),
    changeRepoSpecs: vi.fn(),
  },
}));

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "pk-1" } })),
}));

describe("github-info query hooks", () => {
  beforeEach(() => vi.clearAllMocks());

  it("useGithubVerification fetches when code and project key exist", async () => {
    vi.mocked(githubInfoService.verifyAuthorization).mockResolvedValue("tok");
    const { result } = renderHook(() => useGithubVerification("code"), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(githubInfoService.verifyAuthorization).toHaveBeenCalledWith(
      "code",
      "pk-1",
    );
  });

  it("useGithubVerification is disabled without a code", () => {
    const { result } = renderHook(() => useGithubVerification(""), {
      wrapper: createWrapper(),
    });
    expect(result.current.fetchStatus).toBe("idle");
  });

  it("useValidateAuthorization queries authorization state", async () => {
    vi.mocked(githubInfoService.checkAlreadyAuthorization).mockResolvedValue({
      isSuccess: true,
    });
    const { result } = renderHook(() => useValidateAuthorization(), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("useRevokeAccess is disabled until explicitly triggered", () => {
    const { result } = renderHook(() => useRevokeAccess(), {
      wrapper: createWrapper(),
    });
    expect(result.current.fetchStatus).toBe("idle");
  });

  it("useGetGithubRepos fetches when verification succeeded", async () => {
    vi.mocked(githubInfoService.getGithubRepos).mockResolvedValue({
      data: { items: [], total_count: 0 },
    } as never);
    const { result } = renderHook(() => useGetGithubRepos(true, "s", 1, 20), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(githubInfoService.getGithubRepos).toHaveBeenCalledWith(
      "pk-1",
      "s",
      1,
      20,
    );
  });

  it("useGetGithubRepos is disabled when verification failed", () => {
    const { result } = renderHook(() => useGetGithubRepos(false), {
      wrapper: createWrapper(),
    });
    expect(result.current.fetchStatus).toBe("idle");
  });

  it("useGetRepositoryUser fetches when enabled", async () => {
    vi.mocked(githubInfoService.getRepositoryUser).mockResolvedValue({
      login: "u",
    } as never);
    const { result } = renderHook(() => useGetRepositoryUser(true), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("useGithubBranches fetches for a repo", async () => {
    vi.mocked(githubInfoService.getGithubBranches).mockResolvedValue([]);
    const { result } = renderHook(() => useGithubBranches("repo"), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(githubInfoService.getGithubBranches).toHaveBeenCalledWith(
      "repo",
      "pk-1",
    );
  });

  it("useRepoAndGitBranchMatch fetches for a repo id", async () => {
    vi.mocked(githubInfoService.getRepoAndGitBranchMatch).mockResolvedValue({
      isMatch: true,
    } as never);
    const { result } = renderHook(() => useRepoAndGitBranchMatch("rid"), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("useGetAllProjects fetches for a project id", async () => {
    vi.mocked(githubInfoService.getAllProjects).mockResolvedValue([]);
    const { result } = renderHook(() => useGetAllProjects("pid"), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("useGetAllProjects is disabled without a project id", () => {
    const { result } = renderHook(() => useGetAllProjects(""), {
      wrapper: createWrapper(),
    });
    expect(result.current.fetchStatus).toBe("idle");
  });

  it("useGetAllRepoBuilds fetches for a project id with options", async () => {
    vi.mocked(githubInfoService.getAllRepoBuilds).mockResolvedValue([]);
    const { result } = renderHook(
      () =>
        useGetAllRepoBuilds("pid", {
          refetchOnMount: true,
          refetchOnWindowFocus: true,
          forceRefresh: true,
        }),
      { wrapper: createWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("useGetRepoDetails fetches with a key and repo id", async () => {
    vi.mocked(githubInfoService.getRepoDetails).mockResolvedValue({});
    const { result } = renderHook(() => useGetRepoDetails("pk", "rid"), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("useGetSpecs queries specs", async () => {
    vi.mocked(githubInfoService.getSpecs).mockResolvedValue({});
    const { result } = renderHook(() => useGetSpecs(), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("useGetCardProjectAndBranch fetches for a build id", async () => {
    vi.mocked(githubInfoService.getCardRepoAndBranches).mockResolvedValue(
      {} as never,
    );
    const { result } = renderHook(() => useGetCardProjectAndBranch("bid"), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });
});

describe("github-info mutation hooks", () => {
  beforeEach(() => vi.clearAllMocks());

  it("useRemoveAuthorization mutates and resets cached queries", async () => {
    vi.mocked(githubInfoService.removeAuthorization).mockResolvedValue({
      isSuccess: true,
    });
    const { result } = renderHook(() => useRemoveAuthorization(), {
      wrapper: createWrapper(),
    });
    result.current.mutate(undefined as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("useInitialRepoDeployment mutates", async () => {
    vi.mocked(githubInfoService.repoInitialDeploy).mockResolvedValue({});
    const { result } = renderHook(() => useInitialRepoDeployment(), {
      wrapper: createWrapper(),
    });
    result.current.mutate({} as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("useManualDeployment mutates", async () => {
    vi.mocked(githubInfoService.manualDeploy).mockResolvedValue({});
    const { result } = renderHook(() => useManualDeployment(), {
      wrapper: createWrapper(),
    });
    result.current.mutate({} as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("useChangeBuildSpecs mutates", async () => {
    vi.mocked(githubInfoService.changeBuildSpecs).mockResolvedValue({});
    const { result } = renderHook(() => useChangeBuildSpecs(), {
      wrapper: createWrapper(),
    });
    result.current.mutate({} as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("useChangeRepoSpecs mutates", async () => {
    vi.mocked(githubInfoService.changeRepoSpecs).mockResolvedValue({});
    const { result } = renderHook(() => useChangeRepoSpecs(), {
      wrapper: createWrapper(),
    });
    result.current.mutate({} as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("mutation error handlers log without throwing", async () => {
    vi.mocked(githubInfoService.repoInitialDeploy).mockRejectedValue(
      new Error("x"),
    );
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    const { result } = renderHook(() => useInitialRepoDeployment(), {
      wrapper: createWrapper(),
    });
    result.current.mutate({} as never);
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(spy).toHaveBeenCalled();
  });
});
