import { useEffect } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { projectService } from "@/services/project.service";
import { projectService as crossProjectService } from "@blocks-identifier/services/project.service";
import { useProjectStore } from "@/store/useProjectStore";
import {
  useCreateProjectFormState,
  shortGuidGenerator,
} from "@/components/create-project/utils";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";

export const useGetProjects = (tenantGroupId = "") => {
  const { setProjects, selectedProject, setSelectedProject } = useProjectStore();

  const query = useQuery({
    queryKey: ["identifier", "projects", tenantGroupId],
    queryFn: () => projectService.getProjects(0, 100, tenantGroupId),
    staleTime: 5 * 60 * 1000, // 5 minutes - prevent unnecessary refetches during navigation
  });

  useEffect(() => {
    if (!query.data) return;
    const flattenedProjects = query.data.flatMap((group) => group.projects);
    setProjects(flattenedProjects);
    if (!selectedProject && flattenedProjects.length > 0) {
      setSelectedProject(flattenedProjects[0]);
    }
  }, [query.data, selectedProject, setProjects, setSelectedProject]);

  return query;
};

export const useGetProject = (options: { projectId: string }) => {
  return useQuery({
    queryKey: ["identifier", "project", options],
    queryFn: () => projectService.getProject(options),
    enabled: Boolean(options.projectId),
  });
};

export const useGetAssets = (
  tenantGroupId: string,
  page: number = 0,
  pageSize: number = 12,
  search: string = "",
) => {
  return useQuery({
    queryKey: ["get-assets", tenantGroupId, page, pageSize, search],
    queryFn: () => crossProjectService.getAssets(tenantGroupId),
  });
};

export const useAddAssets = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["assets", "add"],
    mutationFn: crossProjectService.addAssets,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["get-assets"] });
      queryClient.invalidateQueries({ queryKey: ["env-repositories"] });
    },
  });
};

export const useGetEnvRepositories = (projectkey: string) => {
  return useQuery({
    queryKey: ["env-repositories", projectkey],
    queryFn: () => crossProjectService.getEnvRepositories(projectkey),
  });
};

export const useUpdateRepositories = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["env-repositories", "update"],
    mutationFn: crossProjectService.repoUpdate,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["env-repositories"] });
    },
  });
};

export const useUpdateProject = (_: { projectKey: string }) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["identifier", "project-update"],
    mutationFn: (payload: { name: string; tenantGroupId: string }) =>
      crossProjectService.updateTenantGroup(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["identifier", "project"] });
      queryClient.invalidateQueries({ queryKey: ["identifier", "projects"] });
    },
  });
};

export const useUpdateTenantGroup = (_: { tenantGroupId: string }) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["identifier", "project-update-tenant-group"],
    mutationFn: (payload: { name: string; tenantGroupId: string }) =>
      crossProjectService.updateTenantGroup(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["identifier", "projects"] });
      queryClient.invalidateQueries({ queryKey: ["identifier", "project"] });
      queryClient.invalidateQueries({ queryKey: ["get-assets"] });
      queryClient.invalidateQueries({ queryKey: ["env-repositories"] });
    },
  });
};

export const useValidateCNameProject = (options: { projectKey: string }) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["identifier", "projects", "validate cname"],
    mutationFn: crossProjectService.validateCNameProject,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["identifier", "project", options] });
    },
  });
};

export const useDisableProject = (options: { projectKey: string }) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["identifier", "projects", "disable"],
    mutationFn: crossProjectService.disableProject,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["identifier", "project", options] });
      queryClient.invalidateQueries({ queryKey: ["identifier", "projects"] });
    },
  });
};

export const useCreateProject = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["identifier", "projects", "create"],
    mutationFn: crossProjectService.createProject,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["identifier", "projects"] });
      queryClient.invalidateQueries({ queryKey: ["get-assets"] });
      queryClient.invalidateQueries({ queryKey: ["env-repositories"] });
    },
  });
};

export const useGetMigrationStatus = (tenantGroupId: string) => {
  return useQuery({
    queryKey: ["identifier", "migration-status", tenantGroupId],
    queryFn: () => crossProjectService.getMigrationStatus(tenantGroupId),
  });
};

export const useProjectForm = () => {
  const navigate = useNavigate();
  const { isPending, mutateAsync } = useCreateProject();
  const { formData, resetFormData } = useCreateProjectFormState();
  const { setTennantGroup, setSelectedProject } = useProjectStore();
  const queryClient = useQueryClient();

  const saveProject = async () => {
    try {
      const environments = formData[2]?.environments || [];
      const shortGuid = shortGuidGenerator(5);
      const baseDomain = import.meta.env.BLOCKS_BASE_DOMAIN || "seliseblocks.com";
      const applicationContexts =
        environments.map((env: { value: string }) => ({
          environment: env.value,
          domain: `https://${env.value === "main" ? "" : env.value}-${shortGuid}.${baseDomain}`,
          cookieDomain: baseDomain,
        })) || [];

      const assets = formData[1]?.assets || [];

      const response = await mutateAsync({
        name: formData[0].name,
        isAcceptBlocksTerms: formData[0].isAcceptBlocksTerms,
        isUseBlocksExclusively: formData[0].isUseBlocksExclusively,
        isProduction: false,
        resources: assets.map((asset) => ({
          name: asset.full_name,
          link: asset.html_url,
          resourceId: asset.id !== undefined ? String(asset.id) : "",
        })),
        applicationContexts,
      });
      if (response?.isSuccess) {
        showSuccessToast({ description: "Your project has been created." });
        setTennantGroup(response.tenantGroupId);

        try {
          const projectGroups = await queryClient.fetchQuery({
            queryKey: ["identifier", "projects", response.tenantGroupId],
            queryFn: () => projectService.getProjects(0, 100, response.tenantGroupId),
            staleTime: 0,
          });

          if (
            projectGroups &&
            projectGroups.length > 0 &&
            projectGroups[0].projects &&
            projectGroups[0].projects.length > 0
          ) {
            setSelectedProject(projectGroups[0].projects[0]);
          }
        } catch {
          showErrorToast({ errors: response.errors });
        }

        navigate("/project-overview");
        resetFormData();
      } else {
        showErrorToast({ errors: response.errors });
      }
    } catch (error: unknown) {
      if (error && typeof error === "object" && "errors" in error) {
        showErrorToast({ errors: (error as { errors: unknown }).errors });
      }
    }
  };

  return {
    isPending,
    saveProject,
  };
};