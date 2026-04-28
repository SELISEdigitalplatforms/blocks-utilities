import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  IGetPermissionByIdPayload,
  IGetPermissionsSeverityRequestPayload,
  IGetResourceGroupPayload,
  IPermissionFilter,
} from "@blocks-idp/iam/models/permission";
import { iamService } from "@blocks-idp/iam/services/iam.service";

export const useGetPermissions = (options: IPermissionFilter) => {
  return useQuery({
    queryKey: ["permissions", options],
    queryFn: () =>
      iamService.permission.getPermissions({
        page: options.page,
        pageSize: options.pageSize,
        projectKey: options.projectKey,
        roles: options.roles,
        ...(options.sort && {
          sort: { property: options.sort.property, isDescending: options.sort.isDescending },
        }),
        filter: {
          search: options.search || "",
          isBuiltIn: options.isBuiltIn,
          resourceGroup: options.resourceGroup || "",
          ...(options.type && { type: options.type }),
          ...(options.permissionSeverity && { permissionSeverity: Number(options.permissionSeverity) }),
        },
      }),
    placeholderData: keepPreviousData,
  });
};

export const useGetPermissionById = (options: IGetPermissionByIdPayload) => {
  return useQuery({
    queryKey: ["permission", options],
    queryFn: () => iamService.permission.getPermissionById(options),
  });
};

export const useAddPermission = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["permission", "add"],
    mutationFn: iamService.permission.addPermission,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["permissions"] });
    },
  });
};

export const useUpdatePermission = (option: IGetPermissionByIdPayload) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["permission", "update"],
    mutationFn: iamService.permission.updatePermission,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["permission", option] });
      queryClient.invalidateQueries({ queryKey: ["permissions"] });
    },
  });
};

export const useGetResourceGroup = (options: IGetResourceGroupPayload) => {
  return useQuery({
    queryKey: ["permissions-resource-group", options],
    queryFn: () => iamService.permission.getResourceGroup(options),
  });
};

export const useGetPermissionsGroupBySeverity = (options: IGetPermissionsSeverityRequestPayload) => {
  return useQuery({
    queryKey: ["permissions-group-by-severity", options],
    queryFn: () => iamService.permission.getPermissionsSeverity(options),
  });
};
