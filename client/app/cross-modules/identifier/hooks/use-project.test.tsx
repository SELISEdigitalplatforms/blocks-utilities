import { renderHook, waitFor, act } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { useProjectStore } from "@seliseblocks/blocks-kit";

const navigate = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual =
    await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return { ...actual, useNavigate: () => navigate };
});

vi.mock("@blocks-identifier/services/project.service", () => ({
  projectService: {
    getProjects: vi.fn(),
    getProject: vi.fn(),
    getAssets: vi.fn(),
    addAssets: vi.fn(),
    getEnvRepositories: vi.fn(),
    repoUpdate: vi.fn(),
    updateTenantGroup: vi.fn(),
    validateCNameProject: vi.fn(),
    disableProject: vi.fn(),
    createProject: vi.fn(),
    getMigrationStatus: vi.fn(),
  },
}));

const resetFormData = vi.fn();
let formData: Record<number, unknown> = {};
vi.mock("@/components/create-project/utils", () => ({
  useCreateProjectFormState: () => ({ formData, resetFormData }),
  shortGuidGenerator: () => "abcde",
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

import { projectService } from "@blocks-identifier/services/project.service";
import {
  useGetProjects,
  useGetProject,
  useGetAssets,
  useAddAssets,
  useGetEnvRepositories,
  useUpdateRepositories,
  useUpdateProject,
  useUpdateTenantGroup,
  useValidateCNameProject,
  useDisableProject,
  useCreateProject,
  useGetMigrationStatus,
  useProjectForm,
} from "./use-project";

const makeWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0 },
      mutations: { retry: false },
    },
  });
  const Wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  );
  Wrapper.displayName = "TestProjectWrapper";
  return Wrapper;
};

describe("use-project hooks", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    formData = {};
    useProjectStore.setState({
      projects: [],
      selectedProject: null,
      selectedTenantGroup: null,
    });
  });

  it("useGetProjects flattens groups and selects the first project", async () => {
    vi.mocked(projectService.getProjects).mockResolvedValue([
      { projects: [{ itemId: "p1" }, { itemId: "p2" }] },
    ] as never);
    const { result } = renderHook(() => useGetProjects("tg1"), {
      wrapper: makeWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    await waitFor(() =>
      expect(useProjectStore.getState().selectedProject).toEqual({
        itemId: "p1",
      }),
    );
    expect(useProjectStore.getState().projects).toHaveLength(2);
  });

  it("useGetProject is disabled without a projectId", async () => {
    const { result } = renderHook(
      () => useGetProject({ projectId: "" }),
      { wrapper: makeWrapper() },
    );
    expect(result.current.fetchStatus).toBe("idle");
    expect(projectService.getProject).not.toHaveBeenCalled();
  });

  it("useGetAssets fetches assets", async () => {
    vi.mocked(projectService.getAssets).mockResolvedValue({ data: [] } as never);
    const { result } = renderHook(() => useGetAssets("tg1"), {
      wrapper: makeWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(projectService.getAssets).toHaveBeenCalledWith("tg1");
  });

  it("useAddAssets mutation resolves", async () => {
    vi.mocked(projectService.addAssets).mockResolvedValue({} as never);
    const { result } = renderHook(() => useAddAssets(), {
      wrapper: makeWrapper(),
    });
    await act(async () => {
      await result.current.mutateAsync({} as never);
    });
    expect(projectService.addAssets).toHaveBeenCalled();
  });

  it("useGetEnvRepositories fetches", async () => {
    vi.mocked(projectService.getEnvRepositories).mockResolvedValue({} as never);
    const { result } = renderHook(() => useGetEnvRepositories("pk"), {
      wrapper: makeWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(projectService.getEnvRepositories).toHaveBeenCalledWith("pk");
  });

  it("useUpdateRepositories mutation resolves", async () => {
    vi.mocked(projectService.repoUpdate).mockResolvedValue({} as never);
    const { result } = renderHook(() => useUpdateRepositories(), {
      wrapper: makeWrapper(),
    });
    await act(async () => {
      await result.current.mutateAsync({} as never);
    });
    expect(projectService.repoUpdate).toHaveBeenCalled();
  });

  it("useUpdateProject and useUpdateTenantGroup call updateTenantGroup", async () => {
    vi.mocked(projectService.updateTenantGroup).mockResolvedValue({} as never);
    const { result: r1 } = renderHook(
      () => useUpdateProject({ projectKey: "pk" }),
      { wrapper: makeWrapper() },
    );
    await act(async () => {
      await r1.current.mutateAsync({ name: "n", tenantGroupId: "tg" });
    });
    const { result: r2 } = renderHook(
      () => useUpdateTenantGroup({ tenantGroupId: "tg" }),
      { wrapper: makeWrapper() },
    );
    await act(async () => {
      await r2.current.mutateAsync({ name: "n", tenantGroupId: "tg" });
    });
    expect(projectService.updateTenantGroup).toHaveBeenCalledTimes(2);
  });

  it("useValidateCNameProject and useDisableProject mutate", async () => {
    vi.mocked(projectService.validateCNameProject).mockResolvedValue({} as never);
    vi.mocked(projectService.disableProject).mockResolvedValue({} as never);
    const { result: v } = renderHook(
      () => useValidateCNameProject({ projectKey: "pk" }),
      { wrapper: makeWrapper() },
    );
    await act(async () => {
      await v.current.mutateAsync({} as never);
    });
    const { result: d } = renderHook(
      () => useDisableProject({ projectKey: "pk" }),
      { wrapper: makeWrapper() },
    );
    await act(async () => {
      await d.current.mutateAsync({} as never);
    });
    expect(projectService.validateCNameProject).toHaveBeenCalled();
    expect(projectService.disableProject).toHaveBeenCalled();
  });

  it("useCreateProject mutates", async () => {
    vi.mocked(projectService.createProject).mockResolvedValue({} as never);
    const { result } = renderHook(() => useCreateProject(), {
      wrapper: makeWrapper(),
    });
    await act(async () => {
      await result.current.mutateAsync({} as never);
    });
    expect(projectService.createProject).toHaveBeenCalled();
  });

  it("useGetMigrationStatus fetches", async () => {
    vi.mocked(projectService.getMigrationStatus).mockResolvedValue({} as never);
    const { result } = renderHook(() => useGetMigrationStatus("tg"), {
      wrapper: makeWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(projectService.getMigrationStatus).toHaveBeenCalledWith("tg");
  });

  it("useProjectForm.saveProject creates a project and navigates on success", async () => {
    formData = {
      0: { name: "Proj", isAcceptBlocksTerms: true, isUseBlocksExclusively: false },
      1: { assets: [{ full_name: "a", html_url: "u", id: 1 }] },
      2: { environments: [{ value: "main" }] },
    };
    vi.mocked(projectService.createProject).mockResolvedValue({
      isSuccess: true,
      tenantGroupId: "tg9",
      errors: [],
    } as never);
    vi.mocked(projectService.getProjects).mockResolvedValue([
      { projects: [{ itemId: "np" }] },
    ] as never);

    const { result } = renderHook(() => useProjectForm(), {
      wrapper: makeWrapper(),
    });
    await act(async () => {
      await result.current.saveProject();
    });

    expect(projectService.createProject).toHaveBeenCalled();
    expect(showSuccessToast).toHaveBeenCalled();
    expect(useProjectStore.getState().selectedTenantGroup).toBe("tg9");
    expect(navigate).toHaveBeenCalledWith("/app/project/tg9/environments");
    expect(resetFormData).toHaveBeenCalled();
  });

  it("useProjectForm.saveProject shows an error toast when creation is not successful", async () => {
    formData = {
      0: { name: "Proj", isAcceptBlocksTerms: true, isUseBlocksExclusively: false },
      1: { assets: [] },
      2: { environments: [] },
    };
    vi.mocked(projectService.createProject).mockResolvedValue({
      isSuccess: false,
      errors: { name: "taken" },
    } as never);

    const { result } = renderHook(() => useProjectForm(), {
      wrapper: makeWrapper(),
    });
    await act(async () => {
      await result.current.saveProject();
    });
    expect(showErrorToast).toHaveBeenCalledWith({ errors: { name: "taken" } });
    expect(navigate).not.toHaveBeenCalled();
  });
});
