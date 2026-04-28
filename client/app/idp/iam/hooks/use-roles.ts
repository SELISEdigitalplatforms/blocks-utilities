import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { roleService } from "@blocks-idp/iam/services/role.service";
import { GetRolesPayload } from "@blocks-idp/iam/models/role";

export const useGetRoles = (option: GetRolesPayload) => {
  return useQuery({
    queryKey: ["roles", option],
    queryFn: () => roleService.getRoles(option),
  });
};

export const useGetRoleById = (options: { id: string; projectKey: string }) => {
  return useQuery({
    queryKey: ["role", options],
    queryFn: () => roleService.getRoleById(options),
  });
};

export const useAddRole = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["role", "add"],
    mutationFn: roleService.addRole,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["roles"] });
    },
  });
};

export const useUpdateRole = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["role", "update"],
    mutationFn: roleService.updateRole,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["roles"] });
    },
  });
};

export const useSetRoles = (slug: string) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["permissions", "set roles"],
    mutationFn: roleService.setRoles,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["permissions", slug] });
      queryClient.invalidateQueries({ queryKey: ["roles"] });
    },
  });
};
