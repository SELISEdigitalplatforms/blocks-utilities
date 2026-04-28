import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { IGetOrganizationByIdParams, IOrganizationFilter } from "@blocks-idp/iam/models/organization";
import { IOrganizationConfigPayload } from "@blocks-idp/iam/models/organization-config.model";
import { iamService } from "@blocks-idp/iam/services/iam.service";

export const useGetOrganizations = (options: IOrganizationFilter) => {
  return useQuery({
    queryKey: ["organizations", options],
    queryFn: () =>
      iamService.organization.getOrganizations({
        page: options.page,
        pageSize: options.pageSize,
        projectKey: options.projectKey,
        searchText: options.search,
      }),
    placeholderData: keepPreviousData,
  });
};

export const useGetOrganizationById = (params: IGetOrganizationByIdParams) => {
  return useQuery({
    queryKey: ["organization", params.itemId, params.projectKey],
    queryFn: () => iamService.organization.getOrganizationById(params),
    enabled: !!params.itemId && !!params.projectKey,
  });
};

export const useSaveOrganization = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["organization", "createOrUpdate"],
    mutationFn: iamService.organization.saveOrganization,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["organizations"] });
    },
  });
};

export const useGetOrganizationConfig = (projectKey: string) => {
  return useQuery({
    queryKey: ["organization", "config", projectKey],
    queryFn: () => iamService.organization.getOrganizationConfig(projectKey),
    enabled: !!projectKey,
  });
};

export const useSaveOrganizationConfig = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["organization", "config", "save"],
    mutationFn: (payload: IOrganizationConfigPayload) =>
      iamService.organization.saveOrganizationConfig(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["organization", "config"] });
    },
  });
};
