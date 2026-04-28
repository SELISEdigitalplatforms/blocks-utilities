import { authClientService } from "@blocks-idp/authentication/services/auth-clients.service";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export const useGetAuthClientCredentials = (options: { projectKey: string }) => {
  return useQuery({
    queryKey: ["authentication", "auth-clients", options],
    queryFn: () => authClientService.clients.getClientCredentials(options),
  });
};

export const useSaveAuthClient = (options: { projectKey: string }) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["authentication", "auth-clients", "save"],
    mutationFn: authClientService.clients.saveClientCredential,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["authentication", "auth-clients", options] });
    },
  });
};

export const useDeleteAuthClient = (options: { projectKey: string }) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["authentication", "auth-clients", "delete"],
    mutationFn: authClientService.clients.deleteClientCredential,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["authentication", "auth-clients", options] });
    },
  });
};
