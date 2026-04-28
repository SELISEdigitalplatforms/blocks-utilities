import { authOidc } from "@blocks-idp/authentication/services/auth-clients-oidc.service";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export const useGetAuthOidcCredentials = (options: { projectKey: string }) => {
  return useQuery({
    queryKey: ["authentication", "auth-oidc-list", options],
    queryFn: () => authOidc.clients.getOidcCredentials(options),
    enabled: !!options.projectKey,
  });
};

export const useGetAuthOidcCredential = (options: { projectKey: string; clientId: string }, enabled: boolean = true) => {
  return useQuery({
    queryKey: ["authentication", "auth-oidc", options],
    queryFn: () => authOidc.clients.getOidcCredential(options),
    enabled,
  });
};

export const useSaveAuthOidc = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["authentication", "auth-oidc", "save"],
    mutationFn: authOidc.clients.saveOidcCredential,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["authentication"], exact: false });
    },
  });
};

export const useDeleteAuthOidc = (options: { projectKey: string }) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["authentication", "auth-oidc", "delete"],
    mutationFn: authOidc.clients.deleteOidcCredential,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["authentication", "auth-oidc-list", options] });
      queryClient.invalidateQueries({ queryKey: ["authentication", "auth-oidc", options] });
    },
  });
};
