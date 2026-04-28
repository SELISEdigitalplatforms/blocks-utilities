import { authenticationService } from "@blocks-idp/authentication/services/authentication.service";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export const useGetAuthConfig = (options: { projectKey: string }) => {
  return useQuery({
    queryKey: ["authentication", "auth-config", options],
    queryFn: () => authenticationService.configuration.getConfig(options),
    enabled: !!options.projectKey,
  });
};

export const useSaveAuthConfig = (options: { projectKey: string }) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["authentication", "auth-config", "save"],
    mutationFn: authenticationService.configuration.saveAuthConfig,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["authentication", "auth-config", options] });
    },
  });
};
